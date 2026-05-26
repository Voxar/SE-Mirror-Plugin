using VRage.Render11.Resources;

namespace ClientPlugin;

/// <summary>
/// Result of <see cref="ILcdOffscreenResolver.TryResolve"/>: the LCD's
/// per-surface offscreen RT plus the metadata that lets
/// <see cref="PanelSurface"/> decide whether a cached lookup is still
/// valid.
/// </summary>
internal readonly struct LcdOffscreenInfo
{
    /// <summary>The offscreen as an RTV — what renderers draw into.</summary>
    public readonly IRtvBindable           Rtv;

    /// <summary>The same offscreen as <see cref="IUserGeneratedTexture"/>;
    /// needed for <c>SetTextureReady</c> after writing so the LCD mesh
    /// shader stops sampling its placeholder.</summary>
    public readonly IUserGeneratedTexture  Texture;

    /// <summary>The LCD screen-area's material name. Mirror-mode
    /// rendering needs this to resolve the mesh plane.</summary>
    public readonly string                 MaterialName;

    /// <summary>Which area within the block's
    /// <c>MyRenderComponentScreenAreas</c> was used. Useful when the
    /// requested surface index is out of range and the resolver had to
    /// fall back to the first valid area.</summary>
    public readonly int                    AreaIdx;

    /// <summary>First valid render-object id of the block at resolution
    /// time. <see cref="PanelSurface"/> caches this and invalidates the
    /// cached offscreen when the live id no longer matches (engine
    /// swapped actors, e.g. after a damage/rebuild).</summary>
    public readonly uint                   RenderObjectId;

    public LcdOffscreenInfo(
        IRtvBindable rtv, IUserGeneratedTexture texture,
        string materialName, int areaIdx, uint renderObjectId)
    {
        Rtv             = rtv;
        Texture         = texture;
        MaterialName    = materialName;
        AreaIdx         = areaIdx;
        RenderObjectId  = renderObjectId;
    }
}
