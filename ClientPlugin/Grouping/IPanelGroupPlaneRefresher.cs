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
    /// <summary>Refresh the basis on every mirror-mode group in
    /// <paramref name="groups"/>. Camera-mode groups are skipped
    /// (single member; geometry read fresh by the renderer).</summary>
    void Refresh(IReadOnlyList<PanelGroup> groups);
}
