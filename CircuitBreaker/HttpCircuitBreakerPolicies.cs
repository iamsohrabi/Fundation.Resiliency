using Fundation.Resiliency.Retry;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace Fundation.Resiliency.CircuitBreaker;

// Ref: https://anthonygiretti.com/2019/03/26/best-practices-with-httpclient-and-retry-policies-with-polly-in-net-core-2-part-2/
public static class HttpCircuitBreakerPolicies
{
    public static ResiliencePipeline<HttpResponseMessage> GetHttpCircuitBreakerPolicy(
        ILogger logger,
        ICircuitBreakerPolicyOptions circuitBreakerPolicyConfig)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = HttpPolicyBuilders.GetBaseBuilder(),
                FailureRatio = 1,
                MinimumThroughput = Math.Max(2, circuitBreakerPolicyConfig.RetryCount + 1),
                BreakDuration = TimeSpan.FromSeconds(circuitBreakerPolicyConfig.BreakDuration),
                OnOpened = args =>
                {
                    OnHttpBreak(
                        args.BreakDuration,
                        circuitBreakerPolicyConfig.RetryCount,
                        logger);
                    return default;
                },
                OnClosed = _ =>
                {
                    OnHttpReset(logger);
                    return default;
                }
            })
            .Build();
    }

    private static void OnHttpBreak(
        TimeSpan breakDuration,
        int retryCount,
        ILogger logger)
    {
        logger.LogWarning(
            "Service shutdown during {BreakDuration} after {DefaultRetryCount} failed retries",
            breakDuration,
            retryCount);
    }

    public static void OnHttpReset(ILogger logger)
    {
        logger.LogInformation("Service restarted");
    }
}
