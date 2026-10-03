using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Fundation.Resiliency.Retry;

// Ref: https://anthonygiretti.com/2019/03/26/best-practices-with-httpclient-and-retry-policies-with-polly-in-net-core-2-part-2/
public static class HttpRetryPolicies
{
    public static ResiliencePipeline<HttpResponseMessage> GetHttpRetryPolicy(
        ILogger logger,
        IRetryPolicyOptions retryPolicyConfig)
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = HttpPolicyBuilders.GetBaseBuilder(),
                MaxRetryAttempts = retryPolicyConfig.RetryCount,
                DelayGenerator = args => new ValueTask<TimeSpan?>(
                    ComputeDuration(args.AttemptNumber + 1)),
                OnRetry = args =>
                {
                    OnHttpRetry(
                        args.Outcome,
                        args.RetryDelay,
                        args.AttemptNumber + 1,
                        logger);
                    return default;
                }
            })
            .Build();
    }

    private static void OnHttpRetry(
        Outcome<HttpResponseMessage> outcome,
        TimeSpan timeSpan,
        int retryCount,
        ILogger logger)
    {
        if (outcome.Result != null)
        {
            logger.LogWarning(
                "Request failed with {StatusCode}. Waiting {TimeSpan} before next retry. Retry attempt {RetryCount}",
                outcome.Result.StatusCode,
                timeSpan,
                retryCount);
        }
        else
        {
            logger.LogWarning(
                "Request failed because network failure. Waiting {TimeSpan} before next retry. Retry attempt {RetryCount}",
                timeSpan,
                retryCount);
        }
    }

    private static TimeSpan ComputeDuration(int input)
    {
        return TimeSpan.FromSeconds(Math.Pow(2, input)) + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
    }
}
