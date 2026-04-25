using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ftgo.Auth;

/// <summary>Probes the downstream API once on startup, logs the result, then stops the host. Each worker uses the same shape with a different <see cref="IAppTokenProvider"/>.</summary>
public sealed partial class DownstreamProbeService(
    DownstreamApiClient client,
    IOptions<DownstreamApiOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<DownstreamProbeService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        try
        {
            var (status, body) = await client.ProbeAsync(opts.ProbePath, opts.Scope, stoppingToken);
            LogResult(logger, status, body);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
#pragma warning disable CA1031 // Probe is the entire purpose of this BackgroundService; we surface failures via exit code + logs and must not let the host crash silently.
        catch (Exception ex)
        {
            LogProbeFailure(logger, ex);
            Environment.ExitCode = 1;
        }
#pragma warning restore CA1031
        finally
        {
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Probe result. Status={Status} Body={Body}")]
    private static partial void LogResult(ILogger logger, int status, string body);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error,
        Message = "Probe failure")]
    private static partial void LogProbeFailure(ILogger logger, Exception ex);
}
