using System.Net;
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

        // CAE / step-up: surface the claims challenge so the caller can re-acquire with `WithClaims`
        // (or otherwise pass the parameter on its next token request) and retry. Never swallow it —
        // the downstream is telling us the existing token is no longer sufficient.
        // Callers can additionally parse the raw header via
        // Microsoft.Identity.Web.Resource.WwwAuthenticateParameters.CreateFromAuthenticationHeaders().
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var (claims, raw) = TryExtractClaimsChallenge(response.Headers.WwwAuthenticate);
            if (claims is not null)
            {
                LogClaimsChallenge(logger, (int)response.StatusCode, path);
                throw new ClaimsChallengeRequiredException((int)response.StatusCode, claims, raw!);
            }
        }

        return ((int)response.StatusCode, body);
    }

    /// <summary>
    /// Parse <c>WWW-Authenticate</c> challenges for a Bearer challenge of the form
    /// <c>Bearer error="insufficient_claims", claims="&lt;b64url&gt;"</c>. Returns <c>(null,null)</c>
    /// when no claims challenge is present.
    /// </summary>
    internal static (string? Claims, string? Raw) TryExtractClaimsChallenge(
        HttpHeaderValueCollection<AuthenticationHeaderValue> wwwAuthenticate)
    {
        foreach (var header in wwwAuthenticate)
        {
            if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (string.IsNullOrEmpty(header.Parameter))
            {
                continue;
            }

            var raw = header.ToString();
            var claims = ExtractParameter(header.Parameter, "claims");
            if (claims is not null)
            {
                return (claims, raw);
            }
        }

        return (null, null);
    }

    private static string? ExtractParameter(string parameter, string name)
    {
        // RFC 7235 challenge auth-params are comma-separated `name=value` pairs where value may be quoted.
        var parts = SplitTopLevel(parameter, ',');
        foreach (var part in parts)
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }
            var key = part[..eq].Trim();
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var value = part[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }
            return value;
        }
        return null;
    }

    private static List<string> SplitTopLevel(string input, char separator)
    {
        var result = new List<string>();
        var inQuotes = false;
        var start = 0;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == separator && !inQuotes)
            {
                result.Add(input[start..i]);
                start = i + 1;
            }
        }
        result.Add(input[start..]);
        return result;
    }

    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Downstream probe complete. Status={Status} Path={Path}")]
    private static partial void LogProbe(ILogger logger, int status, string path);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Downstream returned claims-challenge. Status={Status} Path={Path}")]
    private static partial void LogClaimsChallenge(ILogger logger, int status, string path);
}
