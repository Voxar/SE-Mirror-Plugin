using System.Collections.Generic;

namespace ClientPlugin;

/// <summary>
/// Builds and caches the list of <see cref="PanelGroup"/> instances
/// covering the current set of registered surfaces. Coplanar same-grid
/// mirror surfaces are merged into one group covering their union
/// AABB; everything else (camera-mode surfaces, ungrouped mirrors)
/// becomes a solo group.
///
/// <para>Cached on <see cref="ISurfaceRegistry.Version"/>: rebuilds
/// only when the surface set or any surface's config changed. Steady-
/// state cost is one int comparison per batch.</para>
/// </summary>
internal interface IPanelGroupBuilder
{
    /// <summary>Get the current group list. The returned list is owned
    /// by the builder; callers may iterate but must not mutate.</summary>
    IReadOnlyList<PanelGroup> GetGroups(ISurfaceRegistry registry);

    /// <summary>Discard the version-cached group list so the next
    /// <see cref="GetGroups"/> call rebuilds from scratch. Used by the
    /// debug "force regroup" action — sometimes the registry version
    /// hasn't changed but the user wants to re-run the merge pass
    /// (e.g. after changing the AlwaysGroupTouching toggle, which
    /// otherwise doesn't trigger a version bump on its own).</summary>
    void InvalidateCache();
}
