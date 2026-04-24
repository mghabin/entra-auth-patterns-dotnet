using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Ftgo.Auth;

/// <summary>
/// Abstraction over "get me an access token for this scope". Each worker implements it
/// with its preferred credential (MI, Cert, FIC, Secret) and is otherwise identical.
/// </summary>
public interface IAppTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken);
}

/// <summary>
/// Typed HTTP client that calls the downstream API. Token-per-call is acquired via
/// <see cref="IAppTokenProvider"/>; the underlying <see cref="HttpClient"/> is resolved
/// via <see cref="IHttpClientFactory"/> so resilience/telemetry middleware apply.
/// </summary>
public sealed partial class DownstreamApiClient(
    HttpClient http,
    IAppTokenProvider tokenProvider,
    ILogger<DownstreamApiClient> logger)
{
    public const string HttpClientName = "downstream";

    public async Task<(int Status, string Body)> ProbeAsync(
        string path,
        string scope,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(scope, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        LogProbe(logger, (int)response.StatusCode, path);
        return ((int)response.StatusCode, body);
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Downstream probe complete. Status={Status} Path={Path}")]
    private static partial void LogProbe(ILogger logger, int status, string path);
}
