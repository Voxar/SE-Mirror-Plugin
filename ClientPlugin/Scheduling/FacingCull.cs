using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Drops groups whose viewer is on the back side of the LCD plane:
/// the screen normal points outward, and reflections only make sense
/// from the viewing side. Source of truth is the group's mesh-derived
/// normal (populated by <see cref="PanelGroupPlaneRefresher"/>). For
/// camera-mode groups (no plane), falls back to the block's
/// <c>WorldMatrix.Backward</c> — the same fallback
/// <c>ScreenPlaneResolver</c> uses when mesh introspection fails.
/// </summary>
internal sealed class FacingCull : IPanelCull
{
    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        var lead = group.Members[0].Surface;
        if (lead.Block == null) return false;

        Vector3D origin, normal;
        if (group.Normal.LengthSquared() > 0.5)
        {
            origin = group.Origin;
            normal = group.Normal;
        }
        else
        {
            var wm = lead.Block.WorldMatrix;
            origin = wm.Translation;
            // WorldMatrix.Forward points INTO the screen back for
            // standard SE LCDs; Backward is the outward-facing
            // direction. Same fallback used by
            // ScreenPlaneResolver.ComputeLocal's failure path.
            normal = wm.Backward;
        }
        return Vector3D.Dot(ctx.Eye - origin, normal) > 0;
    }
}
