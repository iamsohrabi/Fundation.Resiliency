using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Fallback;

namespace Fundation.Resiliency.Fallback;

// Ref: https://anderly.com/2019/12/12/cross-cutting-concerns-with-mediatr-pipeline-behaviors/

/// <summary>
/// MediatR Fallback Pipeline Behavior
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public class FallbackBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IFallbackHandler<TRequest, TResponse>> _fallbackHandlers;
    private readonly ILogger<FallbackBehavior<TRequest, TResponse>> _logger;

    public FallbackBehavior(
        IEnumerable<IFallbackHandler<TRequest, TResponse>> fallbackHandlers,
        ILogger<FallbackBehavior<TRequest, TResponse>> logger)
    {
        _fallbackHandlers = fallbackHandlers;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var fallbackHandler = _fallbackHandlers.FirstOrDefault();
        if (fallbackHandler == null)

            // No fallback handler found, continue through pipeline
            return await next();

        var fallbackPipeline = new ResiliencePipelineBuilder<TResponse>()
            .AddFallback(new FallbackStrategyOptions<TResponse>
            {
                ShouldHandle = new PredicateBuilder<TResponse>().Handle<Exception>(),
                FallbackAction = async args =>
                {
                    _logger.LogDebug(
                        "Initial handler failed. Falling back to `{FullName}@HandleFallback`",
                        fallbackHandler.GetType().FullName);
                    var result = await fallbackHandler.HandleFallbackAsync(request, cancellationToken);
                    return Outcome.FromResult(result);
                }
            })
            .Build();

        return await fallbackPipeline.ExecuteAsync(
            async _ => await next(),
            cancellationToken);
    }
}
