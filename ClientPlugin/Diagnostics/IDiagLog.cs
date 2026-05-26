namespace ClientPlugin;

/// <summary>
/// Tiny logging facade so the orchestrator / culls / pipeline don't
/// reach into <c>MyLog</c> directly. Useful for tests (fake impl), and
/// gives one place to add throttling / structured fields without
/// touching call sites.
/// </summary>
internal interface IDiagLog
{
    /// <summary>Log one diagnostic line tagged with a site key.
    /// Implementations may throttle by site so the same log line firing
    /// every frame doesn't drown out other messages.</summary>
    void Log(string site, string message);
}
