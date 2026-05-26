namespace ClientPlugin;

/// <summary>
/// A single member of a <see cref="PanelGroup"/>. Records the surface
/// plus its sub-rectangle in the group's (U, V) plane coords and the
/// in-plane rotation relative to the group's basis (0/1/2/3 =
/// 0°/90°/180°/270° CCW). Used at blit time to extract the right slice
/// of the shared post-process result into each member's LCD offscreen.
///
/// Immutable: the grouping pass rebuilds membership when surfaces
/// register/unregister/change config.
/// </summary>
internal readonly struct GroupMember
{
    public readonly PanelSurface Surface;

    /// <summary>Sub-rect in the group's (U, V) plane coords.</summary>
    public readonly double UMin, UMax, VMin, VMax;

    /// <summary>0/1/2/3 = 0°/90°/180°/270° CCW relative to group basis.</summary>
    public readonly int Rotation;

    public GroupMember(PanelSurface surface,
                       double uMin, double uMax, double vMin, double vMax,
                       int rotation)
    {
        Surface  = surface;
        UMin = uMin; UMax = uMax;
        VMin = vMin; VMax = vMax;
        Rotation = rotation;
    }
}
