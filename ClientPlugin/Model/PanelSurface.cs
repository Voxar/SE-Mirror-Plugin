using System;
using VRage.Render11.Resources;
using VRageMath;
using IMyCubeBlock   = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace ClientPlugin;

/// <summary>
/// One instance per registered LCD surface, owned by
/// <see cref="SurfaceRegistry"/>. Lifetime spans from the mod's
/// registration event to its unregistration. Holds all plugin-side
/// per-surface state: identity, current configuration, scheduling
/// counters, and render-thread caches (offscreen RT, freshest actor
/// matrix, group membership).
///
/// <para>Thread model:</para>
/// <list type="bullet">
///   <item><see cref="Identity"/>: immutable, free to read from any
///         thread.</item>
///   <item><see cref="Config"/>: written by sim thread (via
///         <see cref="UpdateConfig"/> on registry sync), read by render
///         thread. The reference field write itself is atomic; readers
///         observe a consistent snapshot.</item>
///   <item>Render-thread state (<see cref="LastRenderedTick"/>):
///         written and read on the render thread only.</item>
///   <item><see cref="Group"/>: written by <see cref="PanelGroupBuilder"/>
///         on the render thread (between batches), read by renderers
///         within a batch.</item>
/// </list>
/// </summary>
internal sealed class PanelSurface
{
    // ── Identity (immutable) ─────────────────────────────────────────

    public PanelIdentity Identity { get; }
    public IMyTextSurface Surface { get; }

    public IMyCubeBlock Block => Identity.Block;
    public int SurfaceIdx     => Identity.SurfaceIdx;
    public long Key           => Identity.Key;

    // ── Configuration (sim writes, render reads) ─────────────────────

    /// <summary>Current configuration. Replaced atomically by
    /// <see cref="UpdateConfig"/>; readers always see a complete
    /// snapshot.</summary>
    public PanelConfig Config { get; private set; }

    public PanelMode Mode           => Config.Mode;
    public IMyCubeBlock CameraBlock => Config.CameraBlock;
    public float     Zoom           => Config.Zoom;

    // ── Render-thread caches (mutable, render thread only) ───────────
    //
    // Public-internal mutability is deliberate: the renderer classes
    // live in the same assembly and are on the per-frame hot path.
    // Property-accessor overhead is zero (JIT inlines auto-properties),
    // but bare fields make the access pattern explicit AND eliminate
    // any chance of subtle accessor-side-effect changes during a
    // future refactor. See design note in oops.md §perf.

    /// <summary>Tick when this surface was last successfully rendered.
    /// Default <see cref="long.MinValue"/> means "never". Used by the
    /// slot scheduler for staleness scoring.</summary>
    internal long LastRenderedTick = long.MinValue;

    /// <summary>Group this surface currently belongs to, set by
    /// <see cref="PanelGroupBuilder"/>. May be a solo group.</summary>
    internal PanelGroup Group;

    /// <summary>Last status string this surface reported through the
    /// plugin → mod status channel. Used by
    /// <see cref="ModBridgeStatusSink"/> to drop duplicate writes so
    /// per-frame "rendered" → "rendered" updates don't churn the
    /// concurrent dictionary on the mod side. Render thread only.</summary>
    internal string LastReportedStatus;

    /// <summary>
    /// Set at the end of a mirror render: did the player's eye position
    /// fall inside the reflected camera's view frustum at that render?
    /// I.e., would the rendered mirror image have contained the
    /// player's body. The scheduler reads this on subsequent frames as
    /// a small priority bonus — a mirror the player is "in" is likely
    /// being used as a rear-view / self-monitor. Stays stale between
    /// renders by design (user-requested), so a mirror that hasn't
    /// rendered in a while keeps its last value. Mirror surfaces only;
    /// cameras leave it default (false). Render thread only.
    /// </summary>
    internal bool PlayerInReflectionLastRender;

    // ── Diagnostics ──────────────────────────────────────────────────

    /// <summary>Last failure message for this surface; null on success.
    /// Useful for the terminal/diag UI; not used by renderers.</summary>
    public string LastFailure { get; internal set; }

    /// <summary>Count of frames this surface has rendered successfully.</summary>
    public int FramesRendered { get; internal set; }

    // ── Construction / lifecycle ─────────────────────────────────────

    public PanelSurface(PanelIdentity identity, IMyTextSurface surface, PanelConfig config)
    {
        Identity = identity;
        Surface  = surface ?? throw new ArgumentNullException(nameof(surface));
        Config   = config;
    }

    /// <summary>
    /// Update this surface's configuration. Returns true if anything
    /// changed (allows the caller to invalidate downstream caches like
    /// grouping). Sim-thread only.
    /// </summary>
    public bool UpdateConfig(in PanelConfig newConfig)
    {
        if (Config.Equals(newConfig)) return false;
        Config = newConfig;
        return true;
    }

    /// <summary>
    /// Called by <see cref="SurfaceRegistry"/> when the mod
    /// unregisters this surface. Drops references so the renderer
    /// stops touching a panel that's gone away.
    /// </summary>
    public void OnUnregister()
    {
        Group              = null;
        LastReportedStatus = null;
    }

    // ── Render-thread API: scheduling ────────────────────────────────

    /// <summary>Mark this surface as rendered at <paramref name="tick"/>.
    /// Resets staleness scoring. Render thread only.</summary>
    public void MarkRendered(long tick)
    {
        LastRenderedTick = tick;
        FramesRendered++;
        LastFailure = null;
    }

    /// <summary>
    /// Mark this surface as having been picked-and-attempted but the
    /// render failed. Advances the staleness clock so the picker can't
    /// accumulate runaway priority on permanently-broken panels (the
    /// "offscreen not ready" runaway loop where a far-away LCD without
    /// an allocated RT keeps getting picked, failing, and growing
    /// stale until it dominates every batch).
    ///
    /// <para>Does NOT bump <see cref="FramesRendered"/> or clear
    /// <see cref="LastFailure"/> — those still reflect the actual
    /// render outcome. The staleness clock advances anyway so the
    /// picker doesn't keep coming back faster than it should.</para>
    /// </summary>
    public void MarkAttemptFailed(long tick)
    {
        LastRenderedTick = tick;
    }

    /// <summary>
    /// Ticks since the last successful render. Returns the "never
    /// rendered" sentinel (60) when no render has happened yet — small
    /// enough that genuinely stale panels can still beat it as ticks
    /// accumulate, large enough to seed an initial render priority.
    /// </summary>
    public long Staleness(long currentTick)
    {
        const long NeverRenderedSeed = 60L;
        return LastRenderedTick == long.MinValue
            ? NeverRenderedSeed
            : currentTick - LastRenderedTick;
    }

    public override string ToString()
        => $"PanelSurface(block={Block?.EntityId ?? 0}, surf={SurfaceIdx}, mode={Mode})";
}
