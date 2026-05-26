using VRageMath;

namespace ClientPlugin;

/// <summary>
/// An axis-aligned rectangle in a plane's (U, V) coordinates. The
/// rectangle covers (<see cref="UMin"/>..<see cref="UMax"/>) along
/// <see cref="BasisU"/> and (<see cref="VMin"/>..<see cref="VMax"/>)
/// along <see cref="BasisV"/>, offset from <see cref="Origin"/>.
///
/// Unifies the two mirror-camera input shapes:
/// <list type="bullet">
///   <item>single panel: centered rect built from <see cref="WorldScreenPlane"/>
///         (UMin = -halfW, UMax = +halfW, etc.)</item>
///   <item>coplanar group: union AABB rect built from
///         <see cref="PanelGroup"/> (origin = first member's center,
///         UMin/UMax/VMin/VMax = computed union extents)</item>
/// </list>
/// Consumed by <see cref="MirrorCamera.TryBuild"/> — the math is
/// identical regardless of which source produced the rect.
/// </summary>
internal readonly struct MirrorRectInPlane
{
    public readonly Vector3D Origin;
    public readonly Vector3D Normal;
    public readonly Vector3D BasisU;
    public readonly Vector3D BasisV;
    public readonly double   UMin, UMax, VMin, VMax;

    public MirrorRectInPlane(
        Vector3D origin, Vector3D normal,
        Vector3D basisU, Vector3D basisV,
        double uMin, double uMax, double vMin, double vMax)
    {
        Origin = origin;
        Normal = normal;
        BasisU = basisU;
        BasisV = basisV;
        UMin = uMin; UMax = uMax;
        VMin = vMin; VMax = vMax;
    }

    public double Width  => UMax - UMin;
    public double Height => VMax - VMin;

    /// <summary>Center of the rectangle in world coordinates.</summary>
    public Vector3D Center =>
        Origin
        + BasisU * (0.5 * (UMin + UMax))
        + BasisV * (0.5 * (VMin + VMax));

    public double SignedDistanceFrom(Vector3D eye)
        => Vector3D.Dot(eye - Origin, Normal);

    /// <summary>Single-panel centered rect from a world plane.</summary>
    public static MirrorRectInPlane FromCenteredPlane(in WorldScreenPlane p)
        => new(
            p.Center, p.Normal, p.Right, p.Up,
            -p.HalfWidth, +p.HalfWidth,
            -p.HalfHeight, +p.HalfHeight);

    /// <summary>Group rect built from a coplanar group's union AABB.</summary>
    public static MirrorRectInPlane FromGroup(PanelGroup g)
        => new(g.Origin, g.Normal, g.BasisU, g.BasisV,
               g.UMin, g.UMax, g.VMin, g.VMax);
}
