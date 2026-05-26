using HarmonyLib;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches
{
    /// <summary>
    /// Engine-wide replacement of <c>NocullRasterizerState</c> with a
    /// depth-clip-enabled equivalent during panel renders.
    ///
    /// <para>Root cause (see <see cref="PanelClipRasterizerState"/>):
    /// SE constructs every rasterizer state from
    /// <c>default(RasterizerStateDescription)</c> which leaves
    /// <c>IsDepthClipEnabled = false</c>. For the main view this is
    /// invisible because CPU frustum culling eliminates geometry
    /// before submission. For a panel render the near plane sits on
    /// the panel surface — anything spatially between reflected eye
    /// and panel that the CPU cull doesn't eliminate (foliage blades,
    /// billboard particles, ALPHA_MASKED vegetation impostors, GPU
    /// particles, clouds, …) clamps NDC z to 1.0 and rasterizes at
    /// the near plane instead of being clipped, producing leaks
    /// floating over the reflection.</para>
    ///
    /// <para>Rather than patching each subsystem individually,
    /// prefix <c>MyRenderContext.SetRasterizerState</c>: when a panel
    /// render is in progress AND the requested state is
    /// <c>NocullRasterizerState</c>, swap in our clip-enabled
    /// variant. Everything else passes through unchanged.</para>
    ///
    /// <para>Cost: ~100ns of Harmony dispatch per call (one volatile
    /// read + one reference compare in the common no-panel-render
    /// case). SE calls this method O(100) times per frame at most;
    /// ~30µs total — well below noise.</para>
    /// </summary>
    [HarmonyPatch(typeof(MyRenderContext), nameof(MyRenderContext.SetRasterizerState))]
    static class Patch_MyRenderContext_SetRasterizerState
    {
        [HarmonyPrefix]
        static void Prefix(ref IRasterizerState rs)
        {
            if (!PanelRenderScope.IsDrawing) return;
            if (rs != MyRasterizerStateManager.NocullRasterizerState) return;

            var clip = PanelClipRasterizerState.Get();
            if (clip != null) rs = clip;
        }
    }
}
