using VRage.Game.Entity;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Drops groups whose lead surface's block wasn't visible in main view
/// last frame. Uses <see cref="MyRenderProxy.VisibleObjectsRead"/> — the
/// engine's hash of render-object IDs marked visible by main view's
/// cull pass (which includes hardware occlusion-query results, so a
/// block behind a wall is correctly excluded).
///
/// <para>The set is one frame stale (we run pre-DrawGameScene; main
/// view fills VisibleObjectsWrite this frame and swaps to Read at
/// frame end), but that's acceptable — the player can't traverse far
/// enough between frames for the staleness to matter.</para>
///
/// <para>Falls open: if the block has no render-object id yet (just
/// spawned), or the visible set is null (engine not ready), the group
/// passes through. This avoids dropping freshly-registered surfaces
/// while their first main-view cull is still pending.</para>
/// </summary>
internal sealed class OcclusionCull : IPanelCull
{
    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        var visible = MyRenderProxy.VisibleObjectsRead;
        if (visible == null) return true;

        // Group survives iff ANY member was visible last frame —
        // a coplanar wall might have most of its panels behind one
        // wall while a corner pokes into view, and we still want the
        // group to render (the visible corner is what the player sees).
        var members = group.Members;
        for (int i = 0; i < members.Count; i++)
        {
            uint id = FirstRenderObjectId(members[i].Surface);
            if (id == uint.MaxValue) return true;  // not yet rendered — give it a chance
            if (visible.Contains(id)) return true;
        }
        return false;
    }

    /// <summary>First valid render-object id on the block, matching
    /// what <see cref="LcdOffscreenResolver"/> uses as the cache-
    /// invalidation key.</summary>
    private static uint FirstRenderObjectId(PanelSurface surface)
    {
        if (!(surface.Block is MyEntity blockEntity)) return uint.MaxValue;
        var rc = blockEntity.Render;
        if (rc == null) return uint.MaxValue;
        var ids = rc.RenderObjectIDs;
        if (ids == null) return uint.MaxValue;
        for (int i = 0; i < ids.Length; i++)
            if (ids[i] != uint.MaxValue) return ids[i];
        return uint.MaxValue;
    }
}
