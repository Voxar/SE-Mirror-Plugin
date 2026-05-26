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

    public CullContext(Vector3D eye, Vector3D forward,
                       BoundingFrustumD viewFrustum, double lookCosThreshold)
    {
        Eye = eye;
        Forward = forward;
        ViewFrustum = viewFrustum;
        LookCosThreshold = lookCosThreshold;
    }
}
