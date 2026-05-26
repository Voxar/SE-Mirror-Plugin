namespace ClientPlugin;

/// <summary>
/// Read-only view of the plugin's runtime settings. Implemented by
/// <see cref="Config"/> (which also handles persistence via
/// <see cref="Settings.ConfigStorage"/>) and injected into renderer
/// / scheduler services that need to react to configuration changes
/// without coupling to the concrete settings class.
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

    /// <summary>Far clip distance for panel cameras (meters).</summary>
    float PanelFarClipM { get; }

    /// <summary>When true, the engine's directional shadows pass is
    /// suppressed for the duration of each panel render — main view
    /// is unaffected. Lets users opt out of shadow-cascade flicker
    /// that some reflections exhibit when cascades are recomputed
    /// against a different camera each frame.</summary>
    bool DisableShadows { get; }

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

    /// <summary>When true, panels keep rendering even when the game
    /// is paused (Esc menu, etc.). Off by default to free GPU while
    /// the player isn't actively playing.</summary>
    bool RenderOnPauseScreen { get; }
}
