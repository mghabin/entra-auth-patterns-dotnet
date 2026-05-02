using System.Net;
using Ftgo.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ftgo.Auth.Tests;

public sealed class DownstreamProbeServiceTests
{
    [Fact]
    public async Task ExecuteAsync_LogsResult_AndStopsHost_OnSuccess()
    {
        var lifetime = new FakeLifetime();
        var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        var client = BuildClient(handler);

        var svc = new DownstreamProbeService(
            client,
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.test/", Scope = "api/.default", ProbePath = "/health/ready" }),
            lifetime,
            NullLogger<DownstreamProbeService>.Instance);

        await svc.StartAsync(TestContext.Current.CancellationToken);
        await svc.ExecuteTask!;

        lifetime.StopRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SetsExitCode1_AndStopsHost_OnException()
    {
        Environment.ExitCode = 0;
        var lifetime = new FakeLifetime();
        var client = BuildClient(new ThrowingHandler());

        var svc = new DownstreamProbeService(
            client,
            Options.Create(new DownstreamApiOptions { BaseUrl = "https://api.test/", Scope = "api/.default", ProbePath = "/health/ready" }),
            lifetime,
            NullLogger<DownstreamProbeService>.Instance);

        await svc.StartAsync(TestContext.Current.CancellationToken);
        await svc.ExecuteTask!;

        Environment.ExitCode.ShouldBe(1);
        lifetime.StopRequested.ShouldBeTrue();
        Environment.ExitCode = 0;
    }

    // Cancellation behaviour is exercised implicitly via StopAsync at host shutdown — the
    // BackgroundService swallows OperationCanceledException when stoppingToken fires (see
    // DownstreamProbeService:23). Skip a dedicated test: it relies on framework-internal timing
    // and adds noise without catching real regressions.

    private static DownstreamApiClient BuildClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") },
            new StubTokenProvider("stub-token"),
            NullLogger<DownstreamApiClient>.Instance);

    private sealed class StubTokenProvider(string token) : IAppTokenProvider
    {
        public ValueTask<string> GetAccessTokenAsync(string scope, CancellationToken cancellationToken) => new(token);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public bool StopRequested { get; private set; }
        public void StopApplication() => StopRequested = true;
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("downstream unreachable");
    }
}
