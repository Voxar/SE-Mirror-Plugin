using System.Collections.Generic;

namespace ClientPlugin;

/// <summary>
/// Contract for the cross-assembly bridge that exposes the mod's
/// per-surface registry to the plugin. Abstracted behind an interface so
/// <see cref="SurfaceRegistry"/> can be unit-tested with a fake bridge,
/// and so the reflection-based concrete implementation
/// (<see cref="ReflectionModBridge"/>) can be swapped for a different
/// strategy (e.g. shared assembly + direct types) without touching
/// callers.
/// </summary>
internal interface IModBridge
{
    /// <summary>True iff the mod assembly was located, the
    /// API version matched, and the registry's reflection
    /// surface was bound successfully.</summary>
    bool IsResolved { get; }

    /// <summary>Attempt to bind to the mod's PanelRegistry. Idempotent:
    /// safe to call repeatedly; returns the current resolution state.
    /// Called by the registry on session start.</summary>
    bool TryResolve();

    /// <summary>Drop all cached reflection state. Called on session
    /// end so the mod assembly can be GC'd.</summary>
    void ClearCache();

    /// <summary>Enumerate the mod's current panel registry. Returns
    /// an empty sequence when <see cref="IsResolved"/> is false.
    /// Called once per sync (sim-thread) by
    /// <see cref="SurfaceRegistry.Sync"/>.</summary>
    IEnumerable<PanelInfoSnapshot> EnumeratePanels();

    /// <summary>Write a per-panel status string to the mod side. Mod
    /// TSS scripts read this to populate the LCD splash subtitle.
    /// No-op when <see cref="IsResolved"/> is false (older mod
    /// versions without the status writer just see this as a dropped
    /// call). Safe to call from the render thread — the mod-side
    /// store is thread-safe.</summary>
    void WriteStatus(long blockId, int surfaceIdx, string status);
}
