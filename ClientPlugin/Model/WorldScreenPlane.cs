using VRageMath;

namespace ClientPlugin;

/// <summary>
/// LCD screen plane in WORLD coordinates. Centered rectangle: extents
/// are symmetric around <see cref="Center"/> along <see cref="Right"/>
/// and <see cref="Up"/>. Built per render from
/// <see cref="ScreenPlaneInfo"/> (mesh-local, cached) and the block's
/// freshest actor world matrix.
///
/// Coplanar mirror groups never form a centered rectangle (their union
/// AABB is offset from the first member's center), so they bypass this
/// type and go through <see cref="MirrorRectInPlane.FromGroup"/>
/// directly.
/// </summary>
internal readonly struct WorldScreenPlane
{
    public readonly Vector3D Center;
    public readonly Vector3D Normal;
    public readonly Vector3D Right;
    public readonly Vector3D Up;
    public readonly double   HalfWidth;
    public readonly double   HalfHeight;
    public readonly bool     DoubleSided;

    public WorldScreenPlane(
        Vector3D center, Vector3D normal, Vector3D right, Vector3D up,
        double halfWidth, double halfHeight, bool doubleSided)
    {
        Center      = center;
        Normal      = normal;
        Right       = right;
        Up          = up;
        HalfWidth   = halfWidth;
        HalfHeight  = halfHeight;
        DoubleSided = doubleSided;
    }

    /// <summary>Positive when <paramref name="eye"/> is on the outward
    /// (Normal) side of the plane.</summary>
    public double SignedDistanceFrom(Vector3D eye)
        => Vector3D.Dot(eye - Center, Normal);

    /// <summary>
    /// Flip basis along the normal. Used for transparent (double-sided)
    /// LCDs when the viewer is on the back side — the reflection still
    /// works, just with normal/right reversed. Right is flipped (not Up)
    /// so the basis determinant stays the same and winding is unchanged.
    /// </summary>
    public WorldScreenPlane Flipped()
        => new(Center, -Normal, -Right, Up, HalfWidth, HalfHeight, DoubleSided);

    /// <summary>
    /// Transform a mesh-local plane to world coordinates using the
    /// block's freshest world matrix. Basis vectors are re-normalized
    /// after <see cref="Vector3D.TransformNormal"/> in case the matrix
    /// carries non-uniform scale.
    /// </summary>
    public static WorldScreenPlane From(in ScreenPlaneInfo local, in MatrixD blockWorld)
    {
        var c = Vector3D.Transform((Vector3D)local.LocalCenter, blockWorld);
        var n = Vector3D.TransformNormal((Vector3D)local.LocalNormal, blockWorld); n.Normalize();
        var r = Vector3D.TransformNormal((Vector3D)local.LocalRight,  blockWorld); r.Normalize();
        var u = Vector3D.TransformNormal((Vector3D)local.LocalUp,     blockWorld); u.Normalize();
        return new WorldScreenPlane(c, n, r, u, local.HalfWidth, local.HalfHeight, local.DoubleSided);
    }
}
