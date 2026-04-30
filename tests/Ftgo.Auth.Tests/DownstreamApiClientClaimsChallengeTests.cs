using System.Net;
using System.Net.Http.Headers;
using Ftgo.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class DownstreamApiClientClaimsChallengeTests
{
    private sealed class StaticTokenProvider : IAppTokenProvider
    {
        public ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult("test-token");
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private static DownstreamApiClient BuildClient(HttpResponseMessage response)
    {
        var http = new HttpClient(new StubHandler(response)) { BaseAddress = new Uri("https://example.test") };
        return new DownstreamApiClient(http, new StaticTokenProvider(), NullLogger<DownstreamApiClient>.Instance);
    }

    [Fact]
    public async Task ProbeAsync_Throws_WhenDownstream401_HasInsufficientClaimsChallenge()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("denied"),
        };
        resp.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
            "Bearer",
            "error=\"insufficient_claims\", claims=\"eyJhY2Nlc3NfdG9rZW4iOnt9fQ==\""));

        var client = BuildClient(resp);

        var ex = await Should.ThrowAsync<ClaimsChallengeRequiredException>(async () =>
            await client.ProbeAsync("/probe", "api://x/.default", TestContext.Current.CancellationToken));
        ex.StatusCode.ShouldBe(401);
        ex.Claims.ShouldBe("eyJhY2Nlc3NfdG9rZW4iOnt9fQ==");
        ex.WwwAuthenticate.ShouldContain("insufficient_claims");
    }

    [Fact]
    public async Task ProbeAsync_Throws_WhenDownstream403_HasClaimsChallenge()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.Forbidden);
        resp.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue(
            "Bearer",
            "error=\"insufficient_claims\", claims=\"abc123\", error_description=\"step-up needed\""));

        var client = BuildClient(resp);

        var ex = await Should.ThrowAsync<ClaimsChallengeRequiredException>(async () =>
            await client.ProbeAsync("/probe", "scope", TestContext.Current.CancellationToken));
        ex.Claims.ShouldBe("abc123");
    }

    [Fact]
    public async Task ProbeAsync_DoesNotThrow_When401_HasNoClaimsParameter()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("nope"),
        };
        resp.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer", "error=\"invalid_token\""));

        var client = BuildClient(resp);
        var (status, body) = await client.ProbeAsync("/probe", "scope", TestContext.Current.CancellationToken);

        status.ShouldBe(401);
        body.ShouldBe("nope");
    }

    [Fact]
    public async Task ProbeAsync_ReturnsBody_OnSuccess()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        var client = BuildClient(resp);

        var (status, body) = await client.ProbeAsync("/probe", "scope", TestContext.Current.CancellationToken);

        status.ShouldBe(200);
        body.ShouldBe("ok");
    }
}
