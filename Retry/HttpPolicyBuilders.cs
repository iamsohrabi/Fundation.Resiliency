using System.Net;
using Polly;

namespace Fundation.Resiliency.Retry;

public static class HttpPolicyBuilders
{
    public static PredicateBuilder<HttpResponseMessage> GetBaseBuilder()
    {
        return new PredicateBuilder<HttpResponseMessage>()
            .Handle<HttpRequestException>()
            .HandleResult(response =>
                response.StatusCode == HttpStatusCode.RequestTimeout
                || (int)response.StatusCode is >= 500 and <= 599);
    }
}
