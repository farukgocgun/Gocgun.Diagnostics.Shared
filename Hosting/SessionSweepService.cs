using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Drops expired sessions on a timer, so credentials leave memory even when no request arrives.</summary>
internal sealed class SessionSweepService : BackgroundService
{
    private readonly IEnumerable<ISessionSweeper> sweepers;
    private readonly DiagnosticsOptions options;
    private readonly TimeProvider time;
    private readonly ILogger<SessionSweepService> logger;

    public SessionSweepService(IEnumerable<ISessionSweeper> sweepers, DiagnosticsOptions options, TimeProvider time, ILogger<SessionSweepService> logger)
    {
        this.sweepers = sweepers;
        this.options = options;
        this.time = time;
        this.logger = logger;
    }

    internal async Task SweepOnceAsync()
    {
        foreach (var sweeper in sweepers)
        {
            // A failed dispose (for example a broken Redis connection) must not fault the service:
            // a faulted BackgroundService stops the whole host by default.
            try
            {
                await sweeper.SweepAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(ex, "Session sweep failed; the next tick retries.");
                }
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.SessionSweepInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await SweepOnceAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
