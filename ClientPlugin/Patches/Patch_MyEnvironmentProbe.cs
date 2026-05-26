using HarmonyLib;
using VRage.Render11.LightingStage.EnvironmentProbe;

namespace ClientPlugin.Patches
{
    /// <summary>
    /// Bypass MyEnvironmentProbe's per-frame work while a panel render
    /// is in progress on this thread. Without this, the env-probe
    /// cubemap captures whatever the panel's mirror/camera eye sees,
    /// which then bleeds into the main scene's ambient reflections.
    /// Matches the equivalent patch in SE-CameraLCD-Remastered.
    /// </summary>
    [HarmonyPatch(typeof(MyEnvironmentProbe))]
    static class Patch_MyEnvironmentProbe
    {
        [HarmonyPatch(nameof(MyEnvironmentProbe.UpdateCullQuery))]
        [HarmonyPrefix]
        static bool UpdateCullQuery_Prefix() => !PanelRenderScope.IsDrawing;

        [HarmonyPatch(nameof(MyEnvironmentProbe.FinalizeEnvProbes))]
        [HarmonyPrefix]
        static bool FinalizeEnvProbes_Prefix() => !PanelRenderScope.IsDrawing;

        [HarmonyPatch(nameof(MyEnvironmentProbe.UpdateProbe))]
        [HarmonyPrefix]
        static bool UpdateProbe_Prefix() => !PanelRenderScope.IsDrawing;
    }
}
