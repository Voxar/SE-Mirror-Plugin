using System;

namespace ClientPlugin;

/// <summary>
/// Drops groups whose lead surface's block is farther from the viewer
/// than the plugin-wide <see cref="IMirrorPluginSettings.MaxViewDistanceM"/>.
/// Replaces the old per-LCD terminal slider with a single plugin-wide
/// knob — easier to tune and the per-block
/// <see cref="MaxScreenRenderDistanceCull"/> still gives finer
/// per-block-type caps.
/// </summary>
internal sealed class RangeCull : IPanelCull
{
    private readonly IMirrorPluginSettings _settings;
    private readonly IPanelStatusSink      _statusSink;

    public RangeCull(IMirrorPluginSettings settings, IPanelStatusSink statusSink)
    {
        _settings   = settings   ?? throw new ArgumentNullException(nameof(settings));
        _statusSink = statusSink ?? throw new ArgumentNullException(nameof(statusSink));
    }

    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        var lead = group.Members[0].Surface;
        var block = lead.Block;
        if (block == null) return false;

        float range = _settings.MaxViewDistanceM;
        if (range <= 0f) return true;  // unconfigured = no cull

        double distSq = (block.WorldMatrix.Translation - ctx.Eye).LengthSquared();
        if (distSq <= range * range) return true;

        // Out-of-range: push status so the mod's TSS splash subtitle
        // changes ("Out of range"). The subtitle change breaks SE's
        // AreEqual sprite-cache gate so RenderSpritesToTexture
        // re-renders the splash over the plugin's last frame.
        // ModBridgeStatusSink dedupes against LastReportedStatus so
        // the per-frame cost is one string compare per panel.
        _statusSink.Report(lead, "Out of range");
        return false;
    }
}
