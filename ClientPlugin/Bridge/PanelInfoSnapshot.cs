using IMyCubeBlock   = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMyTextSurface = Sandbox.ModAPI.Ingame.IMyTextSurface;

namespace ClientPlugin;

/// <summary>
/// One snapshot of the mod's <c>PanelRegistry.PanelInfo</c>, projected
/// into plugin-side types. Built by <see cref="IModBridge"/> implementations
/// once per <see cref="ISurfaceRegistry.Sync"/> call — never lives past
/// the sync that produced it.
/// </summary>
internal readonly struct PanelInfoSnapshot
{
    public readonly IMyTextSurface Surface;
    public readonly IMyCubeBlock   Block;
    public readonly int            SurfaceIdx;
    public readonly PanelMode      Mode;
    /// <summary>Camera block the panel renders the view of. Null for
    /// Mirror mode. Passed by reference (not entity id) so the plugin
    /// never has to do a per-frame entity-table lookup.</summary>
    public readonly IMyCubeBlock   CameraBlock;
    public readonly float          Zoom;
    /// <summary>Mirror mode: yaw applied to the screen plane normal
    /// before reflection (degrees). 0 for Camera mode.</summary>
    public readonly float          MirrorAngleDegX;
    /// <summary>Mirror mode: pitch applied to the screen plane normal
    /// before reflection (degrees). 0 for Camera mode.</summary>
    public readonly float          MirrorAngleDegY;

    public PanelInfoSnapshot(
        IMyTextSurface surface, IMyCubeBlock block, int surfaceIdx,
        PanelMode mode, IMyCubeBlock cameraBlock, float zoom,
        float mirrorAngleDegX, float mirrorAngleDegY)
    {
        Surface         = surface;
        Block           = block;
        SurfaceIdx      = surfaceIdx;
        Mode            = mode;
        CameraBlock     = cameraBlock;
        Zoom            = zoom;
        MirrorAngleDegX = mirrorAngleDegX;
        MirrorAngleDegY = mirrorAngleDegY;
    }

    public PanelIdentity Identity => new(Block, SurfaceIdx);
    public PanelConfig   Config   => new(Mode, CameraBlock, Zoom, MirrorAngleDegX, MirrorAngleDegY);
}
