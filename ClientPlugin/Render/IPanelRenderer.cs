namespace ClientPlugin;

/// <summary>
/// Strategy interface for rendering one solo surface (mode-specific).
/// Implementations: <see cref="MirrorPanelRenderer"/>,
/// <see cref="CameraPanelRenderer"/>. Selected at dispatch time by
/// <see cref="PanelRendererDispatcher"/> based on
/// <see cref="PanelSurface.Mode"/>.
///
/// <para>Multi-member coplanar mirror groups go through
/// <see cref="MirrorGroupRenderer"/> instead — different input shape
/// (one rect covering multiple surfaces) and different finalize
/// step (per-member sub-rect blit).</para>
/// </summary>
internal interface IPanelRenderer
{
    /// <summary>
    /// Render <paramref name="surface"/> into its own offscreen RT.
    /// Returns true on success. Exceptions are caught inside the
    /// pipeline and logged; renderers don't propagate.
    /// </summary>
    bool Render(PanelSurface surface, in PanelRenderContext ctx);
}
