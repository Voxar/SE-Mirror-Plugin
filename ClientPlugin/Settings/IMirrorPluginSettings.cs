namespace ClientPlugin;

/// <summary>
/// Read-only view of the plugin's runtime settings. Implemented by
/// <c>MirrorCameraPluginSettings</c> (which also handles persistence)
/// and injected into renderer / scheduler services that need to react
/// to configuration changes without coupling to the concrete settings
/// class.
///
/// <para>All properties are expected to return the current value at
/// call time — implementations may freely mutate their backing
/// storage; consumers should read on each use rather than cache.</para>
/// </summary>
internal interface IMirrorPluginSettings
{
    /// <summary>Master toggle. When false the entire batch is skipped
    /// in the DrawGameScene prefix.</summary>
    bool Enabled { get; }

    /// <summary>Maximum panel renders per batch. The slot scheduler
    /// caps its loop at this value.</summary>
    int MaxPerFrame { get; }

    /// <summary>When true, FPV character head/eye materials are made
    /// visible during a panel render so the reflection shows the full
    /// character. When false, those proxies stay engine-masked.</summary>
    bool HeadFix { get; }

    /// <summary>Far clip distance for panel cameras (meters).</summary>
    float PanelFarClipM { get; }

    /// <summary>When true, coplanar same-grid mirror panels whose
    /// edges literally touch (gap ≤ 10cm on each axis) merge into
    /// one group regardless of the proportional RT-size budget. The
    /// engine's main viewport resolution is still the absolute cap
    /// (the render reuses that RT), so a long wall of touching
    /// mirrors merges into one group and renders at reduced effective
    /// per-panel resolution rather than being split into chunks.
    /// Tradeoff the user opts into by building a mirror wall.</summary>
    bool AlwaysGroupTouching { get; }

    /// <summary>When true, the plugin pushes per-panel state strings
    /// back through the mod-bridge (panel found / rendered / failed
    /// reason). The mod's TSS scripts display these as the LCD splash
    /// subtitle. When false, the channel is silent and the mod shows
    /// its default "Plugin not loaded" message.</summary>
    bool ReportStatus { get; }

    /// <summary>When true, panel renders include the engine's
    /// directional shadows pass. When false, shadows are suppressed
    /// for the panel-render duration only — main view is unaffected.
    /// Off can avoid shadow flickering some users see in reflections
    /// where the shadow cascades are recomputed against a different
    /// camera each frame.</summary>
    bool RenderShadows { get; }

    /// <summary>When true, <see cref="PanelDebug.DrawHud"/> overlays
    /// the top-N scored render units onto the screen as
    /// debug text — picked units highlighted, all per-unit scoring
    /// signals visible. Diagnostic tool, leave off in normal use.</summary>
    bool DebugHud { get; }

    /// <summary>When true, panels covering a small fraction of the
    /// main view render at a reduced resolution (bucketed scale chosen
    /// from Coverage). Keeps the LCD's effective angular resolution
    /// roughly constant as the viewer moves — distant panels stop
    /// looking unnaturally crisp because the off-axis frustum has
    /// narrowed but the render buffer was still full-size. Off by
    /// default while we're proving out the engine borrow-pool
    /// interaction.</summary>
    bool DistanceResolutionScale { get; }

    /// <summary>Plugin-wide hard cap on render range (meters). Panels
    /// farther than this from the viewer never render. Replaces the
    /// per-LCD terminal slider — one knob for the whole world.</summary>
    float MaxViewDistanceM { get; }
}
