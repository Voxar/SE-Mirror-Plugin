using Sandbox.Game.Components;
using VRage.Game.Entity;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="ILcdOffscreenResolver"/> implementation. Walks
/// the block's <c>MyRenderComponentScreenAreas.m_screenAreas</c>, picks
/// the entry matching <paramref name="surfaceIdx"/> (with a fallback to
/// the first non-degenerate area if the index is out of range), and
/// looks the offscreen up by name in
/// <see cref="MyManagers.FileTextures"/>.
/// </summary>
internal sealed class LcdOffscreenResolver : ILcdOffscreenResolver
{
    public bool TryResolve(IMyCubeBlock block, int surfaceIdx, out LcdOffscreenInfo info)
    {
        info = default;
        if (!(block is MyEntity blockEntity)) return false;

        var screenRC = blockEntity.Render as MyRenderComponentScreenAreas;
        if (screenRC == null) return false;

        var areas = screenRC.m_screenAreas;
        if (areas == null || areas.Count == 0) return false;

        // Pick area index = surfaceIdx (multi-text-panel blocks
        // register one area per surface, in surface-index order). Fall
        // back to the first area with any valid render-object id if the
        // requested index is out of range, then to area 0 (which may
        // simply not be populated yet — caller retries next frame).
        // Inlined because PanelScreenArea is private-nested in
        // MyRenderComponentScreenAreas and can't be referenced by name.
        int idx = surfaceIdx;
        if (idx < 0 || idx >= areas.Count)
        {
            idx = -1;
            for (int i = 0; i < areas.Count; i++)
            {
                var areaIds = areas[i].RenderObjectIDs;
                if (areaIds == null) continue;
                for (int j = 0; j < areaIds.Length; j++)
                {
                    if (areaIds[j] != uint.MaxValue) { idx = i; break; }
                }
                if (idx >= 0) break;
            }
            if (idx < 0) idx = 0;
        }

        var area = areas[idx];
        string materialName = area.Material;
        string offscreenName = area.OffscreenTextureName;
        if (string.IsNullOrEmpty(offscreenName))
        {
            // SE constructs offscreen names as "LCDOffscreenTexture_{entityId}_{material}".
            // Build the fallback so resolution still works for blocks that
            // don't populate OffscreenTextureName.
            offscreenName = "LCDOffscreenTexture_" + blockEntity.EntityId
                          + "_" + (materialName ?? string.Empty);
        }

        if (!MyManagers.FileTextures.TryGetTexture(offscreenName, out IUserGeneratedTexture tex)
            || tex == null)
            return false;

        // First valid render-object id == the MyActor's id. PanelSurface
        // uses this to invalidate its cached offscreen when the engine
        // swaps actors out (e.g. block rebuilt after damage).
        uint renderObjectId = uint.MaxValue;
        var ids = screenRC.RenderObjectIDs;
        if (ids != null)
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] != uint.MaxValue) { renderObjectId = ids[i]; break; }

        info = new LcdOffscreenInfo(
            rtv: tex, texture: tex,
            materialName: materialName, areaIdx: idx,
            renderObjectId: renderObjectId);
        return true;
    }

}
