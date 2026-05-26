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
    /// Tilt the plane's normal (and rotate the basis to keep it
    /// orthonormal) by <paramref name="yawRad"/> around <see cref="Up"/>
    /// and <paramref name="pitchRad"/> around the post-yaw <see cref="Right"/>,
    /// pivoting around the rect's EDGE in the lean direction (or the
    /// CORNER when both axes lean). The edge — at
    /// <c>+signX·HalfWidth·Right + signY·HalfHeight·Up</c> from
    /// <see cref="Center"/> — stays fixed in world space; the opposite
    /// edge swings backward. Matches the mod's MirrorMeshTilt pivot
    /// strategy so the reflection plane stays attached to the visibly
    /// tilted mesh.
    ///
    /// <para>Yaw is applied before pitch (consistent ordering between
    /// model and plane). Both zero is a no-op shortcut.
    /// <paramref name="signX"/> and <paramref name="signY"/> are the
    /// signs of the input slider angles — pass 0 if the corresponding
    /// axis isn't leaning, in which case the pivot collapses to the
    /// plane centre along that axis (harmless since the corresponding
    /// rotation is also 0).</para>
    /// </summary>
    public WorldScreenPlane Tilted(double yawRad, double pitchRad, int signX, int signY)
    {
        if (yawRad == 0 && pitchRad == 0) return this;

        var n = Normal;
        var r = Right;
        var u = Up;

        // Yaw: rotate (n, r) in the Right/Normal plane around Up.
        if (yawRad != 0)
        {
            double cy = System.Math.Cos(yawRad);
            double sy = System.Math.Sin(yawRad);
            var newN = n * cy + r * sy;
            var newR = r * cy - n * sy;
            n = newN;
            r = newR;
        }

        // Pitch: rotate (n, u) in the Up/Normal plane around the
        // post-yaw Right.
        if (pitchRad != 0)
        {
            double cp = System.Math.Cos(pitchRad);
            double sp = System.Math.Sin(pitchRad);
            var newN = n * cp + u * sp;
            var newU = u * cp - n * sp;
            n = newN;
            u = newU;
        }

        // Edge-pivot translation: the pivot edge sits at the same world
        // position both before and after the rotation. So if the
        // ORIGINAL Center + (signX·HW·Right_orig + signY·HH·Up_orig)
        // equals the NEW Center + (signX·HW·Right_new + signY·HH·Up_new),
        // then NEW Center = ORIGINAL Center + (signX·HW · (Right_orig
        // − Right_new)) + (signY·HH · (Up_orig − Up_new)).
        var newCenter = Center
                      + signX * HalfWidth  * (Right - r)
                      + signY * HalfHeight * (Up    - u);

        return new WorldScreenPlane(newCenter, n, r, u, HalfWidth, HalfHeight, DoubleSided);
    }

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
