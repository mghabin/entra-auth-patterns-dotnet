using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Ftgo.Auth;

/// <summary>Returns an access token for the supplied scope.</summary>
public interface IAppTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken);
}

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
