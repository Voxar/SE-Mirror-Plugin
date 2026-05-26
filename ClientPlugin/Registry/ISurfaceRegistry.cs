namespace ClientPlugin;

/// <summary>
/// Plugin-side registry of <see cref="PanelSurface"/> instances. Owns
/// surface lifecycle (create on first sight, dispose on removal) and
/// exposes a render-thread-safe snapshot for the per-frame batch.
/// </summary>
internal interface ISurfaceRegistry
{
    /// <summary>Monotonically increasing counter; bumps once per
    /// <see cref="Sync"/> call that produced any change (add / remove
    /// / config update). Consumers that cache derived state (grouping,
    /// plane geometry) compare this to their last-seen version to
    /// decide whether to recompute.</summary>
    int Version { get; }

    /// <summary>Reconcile internal state with the mod's current
    /// registry. Sim-thread only; called once per sim tick.</summary>
    void Sync();

    /// <summary>Render-thread-safe snapshot of currently registered
    /// surfaces. The returned array is immutable from the caller's
    /// perspective: <see cref="Sync"/> swaps a new array in atomically
    /// rather than mutating the existing one. Iterate by index.</summary>
    PanelSurface[] SnapshotForRender();

    /// <summary>Look up a surface by identity. Returns null if not
    /// registered. Sim-thread only — render threads use the snapshot.</summary>
    PanelSurface TryGet(PanelIdentity id);
}
