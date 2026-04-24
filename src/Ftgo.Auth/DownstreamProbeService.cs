using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ftgo.Auth;

/// <summary>
/// A <see cref="BackgroundService"/> that probes the downstream API exactly once on
/// startup, logs the result, then triggers <see cref="IHostApplicationLifetime.StopApplication"/>.
/// This is the shape every worker in this sample takes; only the token provider differs.
/// </summary>
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
            // Graceful shutdown — nothing to do.
        }
        catch (HttpRequestException ex)
        {
            LogHttpFailure(logger, ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information,
        Message = "Probe result. Status={Status} Body={Body}")]
    private static partial void LogResult(ILogger logger, int status, string body);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error,
        Message = "Probe HTTP failure")]
    private static partial void LogHttpFailure(ILogger logger, Exception ex);
}
