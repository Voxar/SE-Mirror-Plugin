using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches
{
    /// <summary>
    /// Prefix on <see cref="MyRender11.DrawGameScene"/>: runs the panel
    /// batch before the engine's main view render. Pre-empt slot is
    /// chosen because the panel renders mutate global render state
    /// (matrices, settings, m_resolution) that the main view assumes
    /// is at its own values — running first lets us snapshot main view
    /// state, swap to panel state, render, and restore before the
    /// engine consumes any of it.
    /// </summary>
    [HarmonyPatch]
    static class Patch_MyRender11_DrawGameScene
    {
        [HarmonyPatch(typeof(MyRender11), nameof(MyRender11.DrawGameScene))]
        [HarmonyPrefix]
        public static void MyRender11_DrawGameScene_Prefix()
        {
            // Skip during screenshot capture: the engine drives its own
            // resolution / camera setup for screenshots, and our panel
            // renders would clobber it.
            if (MyRender11.m_screenshot.HasValue) return;

            // Delegate to the orchestrator. The orchestrator owns the
            // settings.Enabled check, so we don't need to gate here.
            Plugin.Current?.Orchestrator?.RunBatch();
        }
    }
}
