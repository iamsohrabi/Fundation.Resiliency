using System.Collections.Concurrent;
using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;

namespace Fundation.Resiliency.Retry;

// Ref: https://anderly.com/2019/12/12/cross-cutting-concerns-with-mediatr-pipeline-behaviors/
public class RetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<RetryBehavior<TRequest, TResponse>> _logger;
    private readonly IEnumerable<IRetryableRequest<TRequest, TResponse>> _retryHandlers;

    public RetryBehavior(
        IEnumerable<IRetryableRequest<TRequest, TResponse>> retryHandlers,
        ILogger<RetryBehavior<TRequest, TResponse>> logger)
    {
        _retryHandlers = retryHandlers;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var retryHandler = request as IRetryableRequest<TRequest, TResponse>
            ?? ResolveRegisteredRetryHandler();
        var retryAttribute = retryHandler == null
            ? Attribute.GetCustomAttribute(typeof(TRequest), typeof(RetryPolicyAttribute)) as RetryPolicyAttribute
            : null;

        if (retryHandler == null && retryAttribute == null)
            return await next();

        var settings = retryHandler is not null
            ? new RetrySettings(
                retryHandler.RetryAttempts,
                retryHandler.RetryDelay,
                retryHandler.RetryWithExponentialBackoff,
                false,
                retryHandler.ExceptionsAllowedBeforeCircuitTrip,
                retryHandler.CircuitBreakDuration)
            : new RetrySettings(
                retryAttribute!.RetryCount,
                retryAttribute.SleepDuration,
                false,
                true,
                retryAttribute.ExceptionsAllowedBeforeCircuitTrip,
                retryAttribute.CircuitBreakDuration);

        var retryPipeline = MediatRRetryPipelineCache<TRequest, TResponse>.GetOrCreate(
            settings,
            _logger);

        return await retryPipeline.ExecuteAsync(
            async _ => await next(),
            cancellationToken);
    }

    private IRetryableRequest<TRequest, TResponse>? ResolveRegisteredRetryHandler()
    {
        var handlers = _retryHandlers.Take(2).ToArray();
        if (handlers.Length > 1)
        {
            throw new InvalidOperationException(
                $"Multiple retry policies are registered for request type '{typeof(TRequest).FullName}'.");
        }

        return handlers.FirstOrDefault();
    }

    internal readonly record struct RetrySettings(
        int RetryCount,
        int DelayMilliseconds,
        bool ExponentialBackoff,
        bool IncrementalBackoff,
        int CircuitBreakThreshold,
        int CircuitBreakDurationSeconds);
}

internal static class MediatRRetryPipelineCache<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<
        RetryBehavior<TRequest, TResponse>.RetrySettings,
        Lazy<ResiliencePipeline<TResponse>>> Pipelines = new();

    public static ResiliencePipeline<TResponse> GetOrCreate(
        RetryBehavior<TRequest, TResponse>.RetrySettings settings,
        ILogger logger)
    {
        return Pipelines.GetOrAdd(
            settings,
            key => new Lazy<ResiliencePipeline<TResponse>>(
                () => CreatePipeline(key, logger),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static ResiliencePipeline<TResponse> CreatePipeline(
        RetryBehavior<TRequest, TResponse>.RetrySettings settings,
        ILogger logger)
    {
        if (settings.RetryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(settings.RetryCount));
        if (settings.DelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(settings.DelayMilliseconds));
        if (settings.CircuitBreakThreshold < 1 || settings.CircuitBreakThreshold == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(settings.CircuitBreakThreshold));
        if (settings.CircuitBreakDurationSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(settings.CircuitBreakDurationSeconds));

        return new ResiliencePipelineBuilder<TResponse>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<TResponse>
            {
                ShouldHandle = new PredicateBuilder<TResponse>()
                    .Handle<Exception>(exception => exception is not OperationCanceledException),
                FailureRatio = 1,
                MinimumThroughput = settings.CircuitBreakThreshold + 1,
                BreakDuration = TimeSpan.FromSeconds(settings.CircuitBreakDurationSeconds),
                OnOpened = args =>
                {
                    logger.LogWarning(
                        "MediatR circuit opened for {BreakDuration} after {FailureThreshold} failed requests",
                        args.BreakDuration,
                        settings.CircuitBreakThreshold + 1);
                    return default;
                },
                OnClosed = _ =>
                {
                    logger.LogInformation("MediatR circuit closed");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions<TResponse>
            {
                ShouldHandle = new PredicateBuilder<TResponse>()
                    .Handle<Exception>(exception => exception is not OperationCanceledException),
                MaxRetryAttempts = settings.RetryCount,
                DelayGenerator = args =>
                {
                    var retryAttempt = args.AttemptNumber + 1;
                    var delay = settings.ExponentialBackoff
                        ? Math.Pow(2, retryAttempt) * settings.DelayMilliseconds
                        : settings.IncrementalBackoff
                            ? retryAttempt * settings.DelayMilliseconds
                        : settings.DelayMilliseconds;
                    var retryDelay = TimeSpan.FromMilliseconds(delay);

                    logger.LogDebug("Retrying, waiting {RetryDelay}...", retryDelay);

                    return new ValueTask<TimeSpan?>(retryDelay);
                }
            })
            .Build();
    }
}
