namespace Ftgo.Auth;

/// <summary>
/// Thrown when a downstream API responds with a Continuous Access Evaluation (CAE) / claims-challenge
/// signal: <c>WWW-Authenticate: Bearer error="insufficient_claims", claims="&lt;b64url&gt;"</c>.
/// </summary>
/// <remarks>
/// Callers should re-acquire a token using <see cref="Claims"/> as the <c>claims</c> parameter on the
/// token request (e.g. MSAL <c>WithClaims</c>) and retry. The raw <c>WWW-Authenticate</c> header is
/// preserved on <see cref="WwwAuthenticate"/> for callers that need to parse additional parameters via
/// <c>Microsoft.Identity.Web.Resource.WwwAuthenticateParameters</c>.
/// </remarks>
public sealed class ClaimsChallengeRequiredException : Exception
{
    public int StatusCode { get; }
    public string Claims { get; }
    public string WwwAuthenticate { get; }

    public ClaimsChallengeRequiredException(int statusCode, string claims, string wwwAuthenticate)
        : base($"Downstream returned {statusCode} with claims-challenge; caller must re-acquire token with the supplied claims.")
    {
        StatusCode = statusCode;
        Claims = claims;
        WwwAuthenticate = wwwAuthenticate;
    }

    public ClaimsChallengeRequiredException() : this(0, string.Empty, string.Empty) { }

    public ClaimsChallengeRequiredException(string message) : base(message)
    {
        Claims = string.Empty;
        WwwAuthenticate = string.Empty;
    }

    public ClaimsChallengeRequiredException(string message, Exception innerException) : base(message, innerException)
    {
        Claims = string.Empty;
        WwwAuthenticate = string.Empty;
    }
}
