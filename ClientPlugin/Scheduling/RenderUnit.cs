using VRageMath;

namespace ClientPlugin;

/// <summary>
/// One scheduling unit: a mirror group (which may be solo) plus its
/// per-batch scoring inputs. The orchestrator pre-allocates an array
/// of these and reuses it across batches.
///
/// <para>Mutable struct on purpose — orchestrator writes to fields
/// directly via array indexing to avoid copying the struct into
/// method args.</para>
/// </summary>
internal struct RenderUnit
{
    /// <summary>The group this unit covers. Never null.</summary>
    public PanelGroup Group;

    /// <summary>Closest-member distance² from viewer (m²). Slot 0
    /// tiebreak; slot 1+ uses it as the score denominator.</summary>
    public double DistSq;

    /// <summary>cos⁴ of the angle between the player's forward and
    /// the nearest point on the group's screen rectangle (or the
    /// anchor for groups without a populated plane). Floored at 0.01.
    /// Measures "how directly is the player aiming at this".</summary>
    public double CenterFactor;

    /// <summary>Fraction of the main camera's screen area this
    /// group's union AABB covers, in [0..1]. Computed by projecting
    /// the 4 AABB corners through main view's ViewProjection into
    /// NDC, Sutherland-Hodgman clipping the resulting quad against
    /// the [-1,1]² viewport, and taking the shoelace area normalized
    /// to [0..1]. Measures "how much of the player's view does this
    /// take up" — a close big mirror dominates a far small one
    /// regardless of where the player is looking.</summary>
    public double Coverage;

    /// <summary>cos⁴ of the angle between player-forward and the
    /// closest point on the group's union AABB rectangle. Floored at
    /// 0.01. Hit inside AABB → 1.0 (the look ray passes through some
    /// part of the rectangle); outside → decays with how far off-rect
    /// the gaze lands. Pairs with <see cref="CenterFactor"/>:
    /// CenterFactor measures angle to the anchor point, LookFactor
    /// measures angle to the rectangle as a whole. For wide multi-
    /// member groups the anchor (lead member's plane center) is at
    /// one edge, so CenterFactor drops when aimed at the middle —
    /// LookFactor stays high because the aim is still inside the
    /// AABB.</summary>
    public double LookFactor;

    /// <summary>The four corners of the group's union AABB in
    /// screen-space NDC, in BL→BR→TR→TL world-rect order. Populated
    /// by <see cref="UnitScorer"/> alongside Coverage. Used by the
    /// panel-vs-panel occlusion pass to test convex-quad containment.
    /// Valid only when <see cref="NdcQuadValid"/> is true.</summary>
    public Vector2D NdcC0;
    public Vector2D NdcC1;
    public Vector2D NdcC2;
    public Vector2D NdcC3;

    /// <summary>Axis-aligned bounding box of the four NDC corners
    /// above. Used as a cheap pre-reject in the panel-vs-panel
    /// occlusion test: if A's AABB doesn't contain B's AABB, A's
    /// quad can't contain B's quad either — skip the 16-cross-product
    /// quad check. Valid only when <see cref="NdcQuadValid"/> is true.</summary>
    public Vector2D NdcMin;
    public Vector2D NdcMax;

    /// <summary>False when the projected quad couldn't be reliably
    /// computed (any corner past the near plane). The unit still
    /// renders normally — Coverage is set to 1.0 so it stays high
    /// priority — but the occlusion pass skips it both as occluder
    /// AND as occlude-ee, since a partially-near-clipped projection
    /// shouldn't claim to cover the entire viewport (that fallback
    /// was wrong: a big mirror wall the player is standing inside
    /// would falsely cull everything else even when facing away).</summary>
    public bool NdcQuadValid;
}
