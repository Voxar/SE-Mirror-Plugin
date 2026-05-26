using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Single source of truth for "the world plane the rest of the plugin
/// should treat this surface as having." Every site that converts a
/// mesh-local <see cref="ScreenPlaneInfo"/> into a world-space
/// <see cref="WorldScreenPlane"/> for a registered panel goes through
/// here.
///
/// <para>The per-surface mirror yaw/pitch enters the world plane
/// IMPLICITLY: the mod's MirrorMeshTilt game-logic component writes
/// the tilted local matrix on the block entity, and the
/// <paramref name="blockWorld"/> passed in here is the
/// actor's freshest world matrix (post-tilt). So this method just
/// applies the standard mesh-local → world transform and the plane
/// comes out at the same position the visibly tilted mesh occupies —
/// no separate tilt math to keep in sync with the mesh.</para>
///
/// <para>Kept as a seam (rather than inlining
/// <see cref="WorldScreenPlane.From"/> at each call site) so a future
/// plane-tilt-without-mesh-tilt case can re-introduce a divergence
/// here without touching the callers.</para>
/// </summary>
internal static class PlaneTiltHelper
{
    public static WorldScreenPlane BuildTilted(
        in ScreenPlaneInfo local, in MatrixD blockWorld,
        PanelConfig config, IMirrorPluginSettings settings)
        => WorldScreenPlane.From(in local, in blockWorld);
}
