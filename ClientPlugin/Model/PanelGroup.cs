using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// A group of coplanar same-grid mirror surfaces that share a plane and
/// can be rendered together: one off-axis projection at the union AABB
/// scale, then per-member rotation-appropriate sub-rect blits into each
/// LCD's own offscreen. Collapses e.g. a 3×3 mirror wall from nine
/// renders per frame to one.
///
/// Lifetime is owned by <see cref="PanelGroupBuilder"/> — instances are
/// rebuilt only when the surface registry version changes; the plane
/// fields (<see cref="Origin"/>, <see cref="Normal"/>, basis) are
/// refreshed in place per frame for moving grids by
/// <see cref="PanelGroupPlaneRefresher"/>.
///
/// Camera-mode surfaces never participate in grouping (each forms a
/// group of one, rendered via <see cref="CameraPanelRenderer"/>).
/// </summary>
internal sealed class PanelGroup
{
    // Plane (mutable: refreshed per frame on moving grids).
    public Vector3D Origin;
    public Vector3D Normal;
    public Vector3D BasisU;
    public Vector3D BasisV;

    // Union AABB in (U, V) plane coords (set at build time).
    public double UMin, UMax, VMin, VMax;

    /// <summary>Entity ID of the cube grid all members live on; groups
    /// never span grids, so this is constant for a group's lifetime.</summary>
    public readonly long GridEntityId;

    /// <summary>Members in registration order. First member is the
    /// anchor — Origin / BasisU / BasisV are derived from its plane.</summary>
    public readonly List<GroupMember> Members;

    public PanelGroup(long gridEntityId, int initialCapacity = 4)
    {
        GridEntityId = gridEntityId;
        Members = new List<GroupMember>(initialCapacity);
    }

    public bool IsSolo => Members.Count == 1;
    public double Width  => UMax - UMin;
    public double Height => VMax - VMin;
}
