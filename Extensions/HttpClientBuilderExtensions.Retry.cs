using Ardalis.GuardClauses;
using Fundation.Core.Extensions;
using Fundation.Resiliency.Retry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace Fundation.Resiliency.Extensions;

public static partial class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddRetryPolicyHandler(
        this IHttpClientBuilder httpClientBuilder)
    {
        // https://stackoverflow.com/questions/53604295/logging-polly-wait-and-retry-policy-asp-net-core-2-1
        httpClientBuilder.AddResilienceHandler(
            nameof(AddRetryPolicyHandler),
            (builder, context) =>
            {
                var services = context.ServiceProvider;
                var options = services.GetRequiredService<IConfiguration>().GetOptions<PolicyOptions>(nameof(PolicyOptions));

                Guard.Against.Null(options, nameof(options));

                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                var retryLogger = loggerFactory.CreateLogger("PollyHttpRetryPoliciesLogger");

                builder.AddPipeline(HttpRetryPolicies.GetHttpRetryPolicy(retryLogger, options));
            });

        return httpClientBuilder;
    }
}
