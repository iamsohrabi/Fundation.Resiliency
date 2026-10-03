using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

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
        var retryHandler = _retryHandlers.FirstOrDefault();

        // var retryAttr = typeof(TRequest).GetCustomAttribute<RetryPolicyAttribute>();
        if (retryHandler == null)

            // No retry handler found, continue through pipeline
            return await next();

        var retryPipeline = new ResiliencePipelineBuilder<TResponse>()
            .AddRetry(new RetryStrategyOptions<TResponse>
            {
                ShouldHandle = new PredicateBuilder<TResponse>().Handle<Exception>(),
                MaxRetryAttempts = retryHandler.RetryAttempts,
                DelayGenerator = args =>
                {
                    var retryAttempt = args.AttemptNumber + 1;
                    var retryDelay = retryHandler.RetryWithExponentialBackoff
                        ? TimeSpan.FromMilliseconds(Math.Pow(2, retryAttempt) * retryHandler.RetryDelay)
                        : TimeSpan.FromMilliseconds(retryHandler.RetryDelay);

                    _logger.LogDebug("Retrying, waiting {RetryDelay}...", retryDelay);

                    return new ValueTask<TimeSpan?>(retryDelay);
                }
            })
            .Build();

        return await retryPipeline.ExecuteAsync(
            async _ => await next(),
            cancellationToken);
    }
}
