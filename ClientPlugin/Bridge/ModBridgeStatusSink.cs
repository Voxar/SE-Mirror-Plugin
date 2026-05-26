using System;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="IPanelStatusSink"/>: forwards status writes
/// through <see cref="IModBridge.WriteStatus"/> to the mod-side
/// PanelRegistry. Drops duplicate status updates per surface so we
/// don't churn the concurrent dictionary on the mod side when the
/// plugin reports the same value every frame (typical "rendered" /
/// "rendered" / "rendered" sequence).
/// </summary>
internal sealed class ModBridgeStatusSink : IPanelStatusSink
{
    private readonly IModBridge            _bridge;
    private readonly IMirrorPluginSettings _settings;

    public ModBridgeStatusSink(IModBridge bridge, IMirrorPluginSettings settings)
    {
        _bridge   = bridge   ?? throw new ArgumentNullException(nameof(bridge));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Report(PanelSurface surface, string status)
    {
        if (surface == null) return;
        if (!_settings.ReportStatus) return;
        if (surface.LastReportedStatus == status) return;          // unchanged → skip
        var blockId = surface.Block?.EntityId ?? 0L;
        if (blockId == 0L) return;
        _bridge.WriteStatus(blockId, surface.SurfaceIdx, status);
        surface.LastReportedStatus = status;
    }
}
