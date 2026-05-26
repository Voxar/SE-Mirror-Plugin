using VRage.Render11.Common;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Snapshot of every render-time engine setting we toggle during a
/// panel render. Encoded as an immutable struct so capture / apply
/// is two simple operations. Each render mode (mirror, camera) has a
/// constructor that produces the "panel render" variant from the main
/// snapshot.
/// </summary>
internal readonly struct RenderSettings
{
    public readonly bool Lodding;
    public readonly bool DrawBillboards;
    public readonly bool EnableShadows;
    public readonly bool ShadowCameraFrozen;
    public readonly bool EnableEyeAdaptation;
    public readonly bool Flares;
    public readonly bool Bloom;
    public readonly bool SSAO;

    public RenderSettings(
        bool lodding, bool drawBillboards,
        bool enableShadows, bool shadowCameraFrozen,
        bool enableEyeAdaptation,
        bool flares, bool bloom, bool ssao)
    {
        Lodding             = lodding;
        DrawBillboards      = drawBillboards;
        EnableShadows       = enableShadows;
        ShadowCameraFrozen  = shadowCameraFrozen;
        EnableEyeAdaptation = enableEyeAdaptation;
        Flares              = flares;
        Bloom               = bloom;
        SSAO                = ssao;
    }

    // ── Factories ────────────────────────────────────────────────────

    /// <summary>Snapshot the engine's current settings.</summary>
    public static RenderSettings CaptureMain() => new(
        lodding:             MyCommon.LoddingSettings.Global.IsUpdateEnabled,
        drawBillboards:      MyRender11.Settings.DrawBillboards,
        enableShadows:       MyRender11.Settings.EnableShadows,
        shadowCameraFrozen:  MyRender11.Settings.ShadowCameraFrozen,
        enableEyeAdaptation: MyRender11.Postprocess.EnableEyeAdaptation,
        flares:              MyRender11.DebugOverrides.Flares,
        bloom:               MyRender11.DebugOverrides.Bloom,
        ssao:                MyRender11.DebugOverrides.SSAO);

    /// <summary>Settings tuned for a mirror render: lodding off
    /// (no LOD churn mid-render), billboards on, shadows frozen (no
    /// cascade recompute), eye-adapt on, post-process extras off.
    /// <paramref name="enableShadows"/> is plumbed from the plugin's
    /// <see cref="IMirrorPluginSettings.RenderShadows"/> toggle —
    /// users can disable shadows in panel renders if cascade
    /// recomputation causes flickering. Inherits other fields from
    /// <paramref name="main"/>.</summary>
    public static RenderSettings ForMirror(in RenderSettings main, bool enableShadows) => new(
        lodding:             false,
        drawBillboards:      true,
        enableShadows:       enableShadows,
        shadowCameraFrozen:  true,
        enableEyeAdaptation: true,
        flares:              false,
        bloom:               false,
        ssao:                false);

    /// <summary>Settings tuned for a camera-block-mode render: as
    /// mirror but with billboards OFF (camera views shouldn't show
    /// HUD-style billboards from the camera's perspective).</summary>
    public static RenderSettings ForCamera(in RenderSettings main, bool enableShadows) => new(
        lodding:             false,
        drawBillboards:      false,
        enableShadows:       enableShadows,
        shadowCameraFrozen:  true,
        enableEyeAdaptation: true,
        flares:              false,
        bloom:               false,
        ssao:                false);

    // ── Apply ────────────────────────────────────────────────────────

    /// <summary>Push these settings to the engine. Touches
    /// <see cref="MyRender11.Settings"/>,
    /// <see cref="MyRender11.Postprocess"/>,
    /// <see cref="MyRender11.DebugOverrides"/>, and the global lodding
    /// settings (which require coordinated writes across managers).</summary>
    public void Apply()
    {
        SetLoddingEnabled(Lodding);
        MyRender11.Settings.DrawBillboards     = DrawBillboards;
        MyRender11.Settings.EnableShadows      = EnableShadows;
        MyRender11.Settings.ShadowCameraFrozen = ShadowCameraFrozen;
        MyRender11.Postprocess.EnableEyeAdaptation = EnableEyeAdaptation;
        MyRender11.DebugOverrides.Flares       = Flares;
        MyRender11.DebugOverrides.Bloom        = Bloom;
        MyRender11.DebugOverrides.SSAO         = SSAO;
    }

    /// <summary>
    /// Mirror of <c>MyRender11.ProcessMessageInternal</c>'s
    /// <c>UpdateNewLoddingSettings</c> case. The lodding flag lives in
    /// three places that must stay in sync: <c>LoddingSettings.Global</c>,
    /// <c>MyManagers.GeometryRenderer.IsLodUpdateEnabled</c>, and
    /// <c>m_globalLoddingSettings</c>; the model factory also needs a
    /// notification. Writing only the top-level field silently leaves
    /// the renderer's cached flag stale.
    /// </summary>
    private static void SetLoddingEnabled(bool enabled)
    {
        var lodding = MyCommon.LoddingSettings;
        var global  = lodding.Global;
        if (global.IsUpdateEnabled == enabled) return;

        global.IsUpdateEnabled              = enabled;
        lodding.Global                      = global;
        MyManagers.GeometryRenderer.IsLodUpdateEnabled        = enabled;
        MyManagers.GeometryRenderer.m_globalLoddingSettings   = global;
        MyManagers.ModelFactory.OnLoddingSettingChanged();
    }
}
