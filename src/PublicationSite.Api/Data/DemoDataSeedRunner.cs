namespace PublicationSite.Api.Data;

/// <summary>
/// Runs <see cref="DemoDataSeeder"/> off the critical path, at startup and whenever the database
/// is reset.
///
/// Building the dataset means several hundred round trips, which is nothing against a database on
/// the same machine and is a minute or two against a hosted one in another region. Doing that
/// before the server starts listening would hold the health check open until it finished, and a
/// platform that polls that endpoint to decide whether a deploy succeeded would conclude it had
/// not. So the application comes up first and the sample data arrives shortly afterwards; the
/// only visible consequence is that someone signing in during those first moments may see a
/// partly built dataset that fills in behind them.
/// </summary>
public class DemoDataSeedRunner(
    IServiceScopeFactory scopeFactory,
    ILogger<DemoDataSeedRunner> logger) : IHostedService
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private CancellationTokenSource? _shutdown;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Tied to the application's lifetime rather than to this call's token, which is only the
        // startup timeout: the seed outlives startup by design.
        _shutdown = new CancellationTokenSource();
        Trigger();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown?.Cancel();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts a seeding run and returns without waiting for it. Safe to call while one is already
    /// in progress — the second waits for the first, finds the marker account and does nothing.
    /// </summary>
    public void Trigger()
    {
        var token = _shutdown?.Token ?? CancellationToken.None;

        _ = Task.Run(async () =>
        {
            await _oneAtATime.WaitAsync(token);
            try
            {
                using var scope = scopeFactory.CreateScope();
                await DemoDataSeeder.SeedAsync(scope.ServiceProvider, token);
            }
            catch (OperationCanceledException)
            {
                // The application is shutting down. Nothing to report and nothing to fix.
            }
            catch (Exception ex)
            {
                // Deliberately not fatal. A missing sample dataset is a nuisance on a testing
                // deployment; refusing to serve because of one would be worse, and the run is
                // idempotent, so a restart retries it.
                logger.LogError(ex, "The demonstration dataset could not be built. Restart to try again.");
            }
            finally
            {
                _oneAtATime.Release();
            }
        }, token);
    }
}
