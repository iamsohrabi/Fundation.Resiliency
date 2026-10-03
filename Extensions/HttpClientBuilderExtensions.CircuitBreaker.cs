using Ardalis.GuardClauses;
using Fundation.Core.Extensions;
using Fundation.Resiliency.CircuitBreaker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;

namespace Fundation.Resiliency.Extensions;

public static partial class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddCircuitBreakerHandler(
        this IHttpClientBuilder httpClientBuilder)
    {
        httpClientBuilder.AddResilienceHandler(
            nameof(AddCircuitBreakerHandler),
            (builder, context) =>
            {
                var services = context.ServiceProvider;
                var options = services.GetRequiredService<IConfiguration>().GetOptions<PolicyOptions>(nameof(PolicyOptions));

                Guard.Against.Null(options, nameof(options));

                var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                var circuitBreakerLogger = loggerFactory.CreateLogger("PollyHttpCircuitBreakerPoliciesLogger");

                builder.AddPipeline(HttpCircuitBreakerPolicies.GetHttpCircuitBreakerPolicy(
                    circuitBreakerLogger,
                    options));
            });

        return httpClientBuilder;
    }
}
