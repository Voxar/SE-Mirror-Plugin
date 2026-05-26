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

    public RangeCull(IMirrorPluginSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        var lead = group.Members[0].Surface;
        var block = lead.Block;
        if (block == null) return false;

        float range = _settings.MaxViewDistanceM;
        if (range <= 0f) return true;  // unconfigured = no cull

        double distSq = (block.WorldMatrix.Translation - ctx.Eye).LengthSquared();
        return distSq <= range * range;
    }
}
