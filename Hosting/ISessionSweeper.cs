namespace Gocgun.Diagnostics.Shared.Hosting;

/// <summary>Drops expired in-memory sessions. <see cref="SessionSweepService"/> calls it on a timer.</summary>
internal interface ISessionSweeper
{
    /// <summary>Removes and disposes sessions whose lifetime has passed.</summary>
    /// <returns>A task that completes when expired sessions are released.</returns>
    ValueTask SweepAsync();
}
