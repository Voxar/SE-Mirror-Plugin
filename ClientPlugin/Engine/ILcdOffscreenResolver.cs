using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace ClientPlugin;

/// <summary>
/// Locates the LCD's render-target offscreen by walking
/// <c>MyRenderComponentScreenAreas</c> on the block and looking the
/// offscreen up by name in the engine's file-texture manager. Stateless
/// — implementations should not cache results (PanelSurface caches
/// with invalidation).
/// </summary>
internal interface ILcdOffscreenResolver
{
    /// <summary>
    /// Resolve the offscreen RT for a (block, surface index) pair.
    /// Returns false when the block has no
    /// <c>MyRenderComponentScreenAreas</c>, its areas are empty, or
    /// the engine hasn't allocated the offscreen yet (LCD hasn't
    /// painted its first frame). The latter is normal at world load
    /// and resolves naturally a few frames later.
    /// </summary>
    bool TryResolve(IMyCubeBlock block, int surfaceIdx, out LcdOffscreenInfo info);
}
