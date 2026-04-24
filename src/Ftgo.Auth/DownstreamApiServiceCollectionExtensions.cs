using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace Ftgo.Auth;

public static class DownstreamApiServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DownstreamApiClient"/> + its backing <see cref="HttpClient"/>
    /// with the standard resilience handler (retry + circuit breaker + timeout).
    /// Caller must separately register an <see cref="IAppTokenProvider"/> impl.
    /// </summary>
    public static IHttpClientBuilder AddEntraAuthDownstreamApi(
        this IServiceCollection services,
        Action<DownstreamApiOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services
            .AddOptions<DownstreamApiOptions>()
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (configureOptions is not null)
        {
            optionsBuilder.Configure(configureOptions);
        }

        var clientBuilder = services
            .AddHttpClient<DownstreamApiClient>(DownstreamApiClient.HttpClientName, (sp, http) =>
            {
                var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<DownstreamApiOptions>>().Value;
                http.BaseAddress = new Uri(opts.BaseUrl);
            });

        clientBuilder.AddStandardResilienceHandler();
        return clientBuilder;
    }
}
