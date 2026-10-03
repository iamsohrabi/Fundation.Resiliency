using Ardalis.GuardClauses;
using Fundation.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Polly;

namespace Fundation.Resiliency.Extensions;

public static partial class HttpClientBuilderExtensions
{
    public static IHttpClientBuilder AddTimeoutHandler(
        this IHttpClientBuilder httpClientBuilder)
    {
        httpClientBuilder.AddResilienceHandler(
            nameof(AddTimeoutHandler),
            (builder, context) =>
            {
                var options = context.ServiceProvider
                    .GetRequiredService<IConfiguration>()
                    .GetOptions<PolicyOptions>(nameof(PolicyOptions));

                Guard.Against.Null(options, nameof(options));
                if (options.TimeOutDuration <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(options.TimeOutDuration),
                        "Timeout duration must be greater than zero.");

                builder.AddTimeout(TimeSpan.FromSeconds(options.TimeOutDuration));
            });

        return httpClientBuilder;
    }
}
