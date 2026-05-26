using VRage.Game.Entity;
using VRage.Render.Scene;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="IActorMatrixSource"/>: walks the entity's render-
/// component ids, finds the first valid <see cref="MyActor"/>, forces a
/// world-matrix update against its parent's last-world, and returns the
/// resulting <c>LastWorldMatrix</c>.
/// </summary>
internal sealed class ActorMatrixSource : IActorMatrixSource
{
    public MatrixD GetFreshestMatrix(MyEntity entity)
    {
        if (entity == null) return MatrixD.Identity;

        MatrixD fallback = entity.WorldMatrix;
        var rc = entity.Render;
        if (rc == null) return fallback;

        uint[] ids = rc.RenderObjectIDs;
        if (ids == null) return fallback;

        uint actorId = uint.MaxValue;
        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] != uint.MaxValue) { actorId = ids[i]; break; }
        }
        if (actorId == uint.MaxValue) return fallback;

        var actor = MyIDTracker<MyActor>.FindByID(actorId);
        if (actor == null) return fallback;

        // Force a recompute of m_lastWorldMatrix from the parent's
        // last-world. Without this the field is one frame stale on
        // child actors when we render in the pre-main-view slot —
        // visible as the panel reflection lagging its own block.
        try { actor.UpdateWorldMatrix(); } catch { /* defensive — engine method shouldn't throw */ }

        return actor.LastWorldMatrix;
    }
}
