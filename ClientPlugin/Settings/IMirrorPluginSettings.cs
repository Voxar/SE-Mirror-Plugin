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

    /// <summary>Multiplier on Coverage in the bucket-scale formula.
    /// Higher = panels stay at high resolution further away, lower =
    /// downscale starts sooner. Range 0.1-5.0; values &gt; 5.0 are the
    /// slider's OFF tick — distance LOD disabled, panels render at
    /// main-view resolution. Default 2.</summary>
    float LodDistanceFactor { get; }

    /// <summary>Plugin-wide hard cap on render range (meters). Panels
    /// farther than this from the viewer never render. Replaces the
    /// per-LCD terminal slider — one knob for the whole world.</summary>
    float MaxViewDistanceM { get; }

    /// <summary>When true, panels keep rendering even when the game
    /// is paused (Esc menu, etc.). Off by default to free GPU while
    /// the player isn't actively playing.</summary>
    bool RenderOnPauseScreen { get; }
}
