using System;
using System.Collections.Generic;
using System.Threading;

namespace ClientPlugin;

/// <summary>
/// Owns the lifecycle of <see cref="PanelSurface"/> instances. The mod's
/// terminal-UI changes flow through <see cref="ReflectionModBridge"/> into the
/// registry once per sim tick; long-lived plugin state (scheduling
/// counters, render-thread caches, group membership) survives across
/// config edits because the same <see cref="PanelSurface"/> instance is
/// reused — only its <see cref="PanelSurface.Config"/> is swapped.
///
/// <para>Thread model:</para>
/// <list type="bullet">
///   <item><see cref="Sync"/> and <see cref="TryGet"/>: sim thread.</item>
///   <item><see cref="SnapshotForRender"/>: render thread (returns the
///         current snapshot reference; Sync swaps it atomically).</item>
/// </list>
/// </summary>
internal sealed class SurfaceRegistry
{
    private readonly ReflectionModBridge _bridge;

    // Master state — sim-thread only.
    private readonly Dictionary<long, PanelSurface> _surfaces = new();
    private readonly HashSet<long>  _seenThisSync = new();
    private readonly List<long>     _scratchRemove = new();

    // Render-visible snapshot. Replaced by Sync as a single volatile
    // reference write — readers always see a fully-populated array.
    // Initial value is non-null so render-side code never null-checks.
    private PanelSurface[] _renderSnapshot = Array.Empty<PanelSurface>();

    private int _version;

    public int Version => Volatile.Read(ref _version);

    private readonly ModBridgeStatusSink _statusSink;

    public SurfaceRegistry(ReflectionModBridge bridge, ModBridgeStatusSink statusSink)
    {
        _bridge     = bridge     ?? throw new ArgumentNullException(nameof(bridge));
        _statusSink = statusSink ?? throw new ArgumentNullException(nameof(statusSink));
    }

    // ── Sim-thread API ────────────────────────────────────────────────

    public void Sync()
    {
        // First-time resolution: try every tick until the mod is loaded.
        // Once resolved, IsResolved stays true until ClearCache is called
        // (which doesn't happen mid-session). Cheap-no-op afterwards.
        if (!_bridge.IsResolved && !_bridge.TryResolve())
        {
            // Mod absent. Drop any state we held (the panels just went
            // away from our perspective).
            if (_surfaces.Count > 0) ClearAll();
            return;
        }

        _seenThisSync.Clear();
        bool changed = false;

        foreach (var snap in _bridge.EnumeratePanels())
        {
            if (snap.Block == null || snap.Surface == null) continue;
            long key = snap.Identity.Key;
            _seenThisSync.Add(key);

            if (_surfaces.TryGetValue(key, out var existing))
            {
                if (existing.UpdateConfig(snap.Config)) changed = true;
            }
            else
            {
                var fresh = new PanelSurface(snap.Identity, snap.Surface, snap.Config);
                _surfaces[key] = fresh;
                _statusSink.Report(fresh, "found");
                changed = true;
            }
        }

        // Mark-and-sweep removal of surfaces no longer in the bridge.
        _scratchRemove.Clear();
        foreach (var kv in _surfaces)
            if (!_seenThisSync.Contains(kv.Key)) _scratchRemove.Add(kv.Key);
        if (_scratchRemove.Count > 0)
        {
            foreach (long k in _scratchRemove)
            {
                if (_surfaces.TryGetValue(k, out var s))
                {
                    s.OnUnregister();
                    _surfaces.Remove(k);
                }
            }
            changed = true;
        }

        if (changed) RebuildSnapshot();
    }

    public PanelSurface TryGet(PanelIdentity id)
    {
        _surfaces.TryGetValue(id.Key, out var s);
        return s;
    }

    // ── Render-thread API ────────────────────────────────────────────

    public PanelSurface[] SnapshotForRender() => Volatile.Read(ref _renderSnapshot);

    // ── Internal ──────────────────────────────────────────────────────

    private void ClearAll()
    {
        foreach (var s in _surfaces.Values) s.OnUnregister();
        _surfaces.Clear();
        _seenThisSync.Clear();
        _scratchRemove.Clear();
        Volatile.Write(ref _renderSnapshot, Array.Empty<PanelSurface>());
        Interlocked.Increment(ref _version);
    }

    /// <summary>Allocate a fresh snapshot array sized exactly to the
    /// current population, publish it via a volatile write, and bump
    /// <see cref="Version"/>. The previous snapshot is left for the
    /// garbage collector — any render thread that captured it before
    /// the swap finishes iterating it safely.</summary>
    private void RebuildSnapshot()
    {
        var arr = new PanelSurface[_surfaces.Count];
        int i = 0;
        foreach (var s in _surfaces.Values) arr[i++] = s;
        Volatile.Write(ref _renderSnapshot, arr);
        Interlocked.Increment(ref _version);
    }
}
