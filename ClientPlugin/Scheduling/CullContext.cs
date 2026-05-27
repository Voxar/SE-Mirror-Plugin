using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Inputs that every visibility cull needs: where the viewer is, where
/// they're looking, the captured view frustum, and the "in look
/// direction" cosine threshold. Captured once per batch by the
/// orchestrator.
/// </summary>
internal readonly struct CullContext
{
    public readonly Vector3D Eye;
    public readonly Vector3D Forward;

    /// <summary>Captured (= deep-copied) view frustum from main view's
    /// matrix at batch start. Null when capture failed; culls that
    /// need a frustum should pass when this is null (don't filter on
    /// missing data).</summary>
    public readonly BoundingFrustumD ViewFrustum;

    /// <summary>cos(angle) lower bound for the "panel is in the
    /// direction the player is looking" test. Typical value ~0.26
    /// (cos 75°) — generous so peripheral mirrors aren't dropped the
    /// instant the player glances away.</summary>
    public readonly double LookCosThreshold;

    /// <summary>Closest-member distance² for the current unit being
    /// evaluated, populated by the orchestrator from
    /// <see cref="RenderUnit.DistSq"/> (computed once per unit per
    /// batch by <see cref="UnitScorer"/>). Distance-based culls
    /// (RangeCull, MaxScreenRenderDistanceCull) read this instead of
    /// recomputing per-call: the lead block's translation alone is
    /// wrong for wide multi-member groups.</summary>
    public readonly double GroupClosestDistSq;

    public CullContext(Vector3D eye, Vector3D forward,
                       BoundingFrustumD viewFrustum, double lookCosThreshold,
                       double groupClosestDistSq = 0.0)
    {
        Eye = eye;
        Forward = forward;
        ViewFrustum = viewFrustum;
        LookCosThreshold = lookCosThreshold;
        GroupClosestDistSq = groupClosestDistSq;
    }

    /// <summary>Return a copy of this context with
    /// <see cref="GroupClosestDistSq"/> set. Used by the orchestrator
    /// to thread the per-unit closest-member distance into the cull
    /// chain without recomputing it in each cull.</summary>
    public CullContext WithGroupClosestDistSq(double distSq)
        => new(Eye, Forward, ViewFrustum, LookCosThreshold, distSq);
}
