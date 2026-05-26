using System.Collections.Generic;
using VRage.Utils;

namespace ClientPlugin;

/// <summary>
/// <see cref="IDiagLog"/> that throttles per call-site to one log
/// entry per N ticks. Prevents a "always fires" message from drowning
/// the engine log every frame at 60 fps.
///
/// <para>Throttling is per-site rather than global so a single frame
/// can still emit multiple lines for different sites (cull stats AND
/// render summary AND offscreen-missing notes) — without per-site
/// throttling, the second call would be swallowed.</para>
/// </summary>
internal sealed class ThrottledDiagLog : IDiagLog
{
    private const int DefaultThrottleTicks = 60;
    private readonly Dictionary<string, long> _lastTickBySite = new();
    private long _tick;

    /// <summary>Bump the internal tick counter. Called by the
    /// orchestrator once per batch.</summary>
    public void AdvanceTick() => _tick++;

    public void Log(string site, string message)
    {
        if (_lastTickBySite.TryGetValue(site, out long last)
            && _tick - last < DefaultThrottleTicks)
            return;

        _lastTickBySite[site] = _tick;
        MyLog.Default.WriteLine("[Mirror/Diag] " + site + ": " + message);
    }
}
