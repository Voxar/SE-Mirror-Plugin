using System.Collections.Generic;

namespace ClientPlugin;

/// <summary>
/// Per-frame refresh of the world-space plane fields on each mirror-
/// mode group (Origin, Normal, BasisU, BasisV). Member UV rects and
/// the union AABB are rigid-motion invariant — only the basis moves
/// with the grid — so this is the cheap-update path that keeps
/// reflections locked to moving ships without forcing
/// <see cref="PanelGroupBuilder"/> to rebuild.
/// </summary>
internal interface IPanelGroupPlaneRefresher
{
    /// <summary>Refresh the basis on every group with a populated
    /// plane (Normal length &gt; 0.5). Plane-less groups — mirrors
    /// whose geometry hasn't resolved yet, or camera-mode panels
    /// kept as no-plane solo fallbacks — are skipped.</summary>
    void Refresh(IReadOnlyList<PanelGroup> groups);
}
