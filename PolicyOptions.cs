using Fundation.Resiliency.CircuitBreaker;
using Fundation.Resiliency.Retry;
using Fundation.Resiliency.Timeout;

namespace Fundation.Resiliency;

// Ref: https://anthonygiretti.com/2019/03/26/best-practices-with-httpclient-and-retry-policies-with-polly-in-net-core-2-part-2/
public class PolicyOptions : ICircuitBreakerPolicyOptions, IRetryPolicyOptions, ITimeoutPolicyOptions
{
    public int RetryCount { get; set; }
    public int BreakDuration { get; set; }
    public int TimeOutDuration { get; set; }
}
