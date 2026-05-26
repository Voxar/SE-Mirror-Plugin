using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Drops groups outside the direction the player is looking. The
/// facing cull only verifies the viewer is on the screen-face side of
/// the plane; a mirror right behind the player satisfies that but
/// obviously isn't visible. Threshold is generous (cosine ≈ 0.26 ≡
/// 75° off-axis) so peripheral mirrors aren't dropped the instant the
/// player glances away.
///
/// <para>For plane-bearing groups the check sweeps the four union-
/// AABB corners and keeps the group as long as ANY corner is inside
/// the look cone. Using the anchor's <c>Origin</c> alone would drop
/// wide multi-member walls the moment their first member's centre
/// drifted off-axis, even with half the wall still filling the
/// player's view.</para>
///
/// <para>Standing exactly at the mirror plane (any-corner distance ≈
/// 0) bypasses the angle check — no meaningful angle exists at
/// distance zero, but the player is close enough that bumping into it
/// is "looking at it" for these purposes; facing + frustum still
/// gate.</para>
/// </summary>
internal sealed class LookDirectionCull : IPanelCull
{
    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        if (group.Normal.LengthSquared() > 0.5)
        {
            // Plane-bearing: test each AABB corner of the union, keep
            // if any is in the cone (or any is essentially at the eye).
            return CornerInCone(group, group.Origin + group.BasisU * group.UMin + group.BasisV * group.VMin, in ctx)
                || CornerInCone(group, group.Origin + group.BasisU * group.UMax + group.BasisV * group.VMin, in ctx)
                || CornerInCone(group, group.Origin + group.BasisU * group.UMax + group.BasisV * group.VMax, in ctx)
                || CornerInCone(group, group.Origin + group.BasisU * group.UMin + group.BasisV * group.VMax, in ctx);
        }

        // No plane (camera-mode fallback): block-translation reference,
        // same logic as before — single point.
        if (group.Members[0].Surface.Block == null) return false;
        return CornerInCone(group, group.Members[0].Surface.Block.WorldMatrix.Translation, in ctx);
    }

    private static bool CornerInCone(PanelGroup _, Vector3D point, in CullContext ctx)
    {
        Vector3D toPoint = point - ctx.Eye;
        double len = toPoint.Length();
        if (len < 0.01) return true;
        toPoint /= len;
        return Vector3D.Dot(ctx.Forward, toPoint) > ctx.LookCosThreshold;
    }
}
