using VRageMath;

namespace ClientPlugin;

/// <summary>
/// LCD screen plane expressed in MESH-LOCAL coordinates. Constant for a
/// given (block definition, screen material) pair across every instance
/// of that LCD type — derived once by <see cref="ScreenPlaneResolver"/>
/// via mesh introspection and cached process-wide.
///
/// To get the world-space plane for a specific block instance, combine
/// with the block's world matrix via
/// <see cref="WorldScreenPlane.From(in ScreenPlaneInfo, in MatrixD)"/>.
/// </summary>
internal readonly struct ScreenPlaneInfo
{
    public readonly Vector3 LocalCenter;
    public readonly Vector3 LocalNormal;
    public readonly Vector3 LocalRight;
    public readonly Vector3 LocalUp;
    public readonly float   HalfWidth;
    public readonly float   HalfHeight;
    public readonly bool    DoubleSided;

    public ScreenPlaneInfo(
        Vector3 localCenter, Vector3 localNormal,
        Vector3 localRight,  Vector3 localUp,
        float halfWidth, float halfHeight, bool doubleSided)
    {
        LocalCenter = localCenter;
        LocalNormal = localNormal;
        LocalRight  = localRight;
        LocalUp     = localUp;
        HalfWidth   = halfWidth;
        HalfHeight  = halfHeight;
        DoubleSided = doubleSided;
    }
}
