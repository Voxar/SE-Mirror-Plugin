using System;
using System.Collections.Generic;
using Sandbox.Game.Components;
using VRage.Game.Entity;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="PanelGroupPlaneRefresher"/>. For each group
/// that carries a screen plane (whether mirror or camera mode), re-
/// derives the world-space basis (Origin, Normal, BasisU, BasisV)
/// from the lead member's freshest world matrix applied to its mesh-
/// local plane. Groups without a populated plane (fallback created
/// by <see cref="PanelGroupBuilder"/> when mesh introspection
/// fails) are skipped — there's nothing to refresh, and trying would
/// just re-attempt the same failed resolve every frame.
/// Cheap: O(groups × const).
/// </summary>
internal sealed class PanelGroupPlaneRefresher
{
    private readonly ScreenPlaneResolver  _planeResolver;
    private readonly ActorMatrixSource    _actorMatrix;
    private readonly IMirrorPluginSettings _settings;

    public PanelGroupPlaneRefresher(
        ScreenPlaneResolver  planeResolver,
        ActorMatrixSource    actorMatrix,
        IMirrorPluginSettings settings)
    {
        _planeResolver = planeResolver ?? throw new ArgumentNullException(nameof(planeResolver));
        _actorMatrix   = actorMatrix   ?? throw new ArgumentNullException(nameof(actorMatrix));
        _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Refresh(IReadOnlyList<PanelGroup> groups)
    {
        if (groups == null) return;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (g.Members.Count == 0) continue;

            // Skip groups that don't carry a plane to begin with —
            // see class docstring. Mode-agnostic: refresh runs for
            // any group whose builder gave it a populated plane.
            if (g.Normal.LengthSquared() <= 0.5) continue;

            var lead = g.Members[0].Surface;
            if (!(lead.Block is MyEntity blockEntity)) continue;

            string material = TryGetFirstScreenMaterial(blockEntity, lead.SurfaceIdx);
            if (string.IsNullOrEmpty(material)) continue;

            if (!_planeResolver.TryResolve(blockEntity, material, out var local)) continue;

            // Actor's freshest world matrix — includes the mod-side
            // mesh tilt (MirrorMeshTilt component writes the tilted
            // local matrix), so the refreshed plane stays attached
            // to the visibly tilted mesh.
            MatrixD blockWorld = _actorMatrix.GetFreshestMatrix(blockEntity);
            var world = WorldScreenPlane.From(in local, in blockWorld);

            g.Origin = world.Center;
            g.Normal = world.Normal;
            g.BasisU = world.Right;
            g.BasisV = world.Up;
        }
    }

    private static string TryGetFirstScreenMaterial(MyEntity blockEntity, int surfaceIdx)
    {
        var screenRC = blockEntity.Render as MyRenderComponentScreenAreas;
        if (screenRC == null) return null;
        var areas = screenRC.m_screenAreas;
        if (areas == null || areas.Count == 0) return null;
        int idx = (surfaceIdx >= 0 && surfaceIdx < areas.Count) ? surfaceIdx : 0;
        return areas[idx].Material;
    }
}
