namespace ClientPlugin;

/// <summary>
/// Renders one coplanar same-grid mirror group: a single reflected-
/// camera render at the union AABB's scale, then a per-member sub-
/// rect blit into each LCD's own offscreen. Used only for multi-
/// member groups; solo groups go through <see cref="IPanelRenderer"/>.
/// </summary>
internal interface IMirrorGroupRenderer
{
    /// <summary>Render <paramref name="group"/> into each member's
    /// offscreen. Returns true if at least one member rendered
    /// successfully; false on viewer-behind-plane or no resolvable
    /// member offscreens.</summary>
    bool Render(PanelGroup group, in PanelRenderContext ctx);
}
