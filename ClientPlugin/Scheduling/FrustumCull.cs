using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Drops groups whose world-space AABB doesn't intersect the captured
/// view frustum. The frustum is built once per batch from a SNAPSHOT
/// of the main view's matrix value — a live reference would get
/// mutated by every <c>SetupCameraMatrices</c> call inside the render
/// loop and start rejecting every group after the first render.
/// </summary>
internal sealed class FrustumCull : IPanelCull
{
    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        if (ctx.ViewFrustum == null) return true;   // no frustum captured → don't filter

        BoundingBoxD bb = ComputeGroupAabb(group);
        if (!bb.Min.IsValid() || !bb.Max.IsValid()) return true;   // can't AABB → don't filter

        try { return ctx.ViewFrustum.Contains(bb) != ContainmentType.Disjoint; }
        catch { return true; }
    }

    /// <summary>World-space AABB enclosing the group's screen
    /// rectangle. Mirror-mode groups with a populated plane use the
    /// exact four corners; camera-mode groups (no plane) fall back
    /// to a 2.5 m box around the lead block.</summary>
    private static BoundingBoxD ComputeGroupAabb(PanelGroup g)
    {
        var bb = BoundingBoxD.CreateInvalid();
        if (g.Normal.LengthSquared() > 0.5)
        {
            bb.Include(g.Origin + g.BasisU * g.UMin + g.BasisV * g.VMin);
            bb.Include(g.Origin + g.BasisU * g.UMin + g.BasisV * g.VMax);
            bb.Include(g.Origin + g.BasisU * g.UMax + g.BasisV * g.VMin);
            bb.Include(g.Origin + g.BasisU * g.UMax + g.BasisV * g.VMax);
            return bb;
        }
        var lead = g.Members.Count > 0 ? g.Members[0].Surface : null;
        if (lead?.Block != null)
        {
            Vector3D t = lead.Block.WorldMatrix.Translation;
            var r = new Vector3D(2.5, 2.5, 2.5);
            bb.Include(t - r);
            bb.Include(t + r);
        }
        return bb;
    }
}
