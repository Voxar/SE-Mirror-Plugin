using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;

namespace ClientPlugin.Patches
{
    /// <summary>
    /// Lazy-created Solid+CullNone rasterizer state with
    /// <c>IsDepthClipEnabled = true</c>. SE's stock
    /// <c>NocullRasterizerState</c> is built from
    /// <c>default(RasterizerStateDescription)</c> and leaves depth-clip
    /// off, which causes blades / billboards / particles past the
    /// camera's near plane to clamp z and rasterize at the plane
    /// instead of being clipped — visible only for panel renders,
    /// where the near plane sits on the panel surface.
    ///
    /// <para>Shared by the foliage and billboard patches. Created
    /// once per process, cached, retried if creation fails (e.g.
    /// the manager isn't ready yet at first call).</para>
    /// </summary>
    internal static class PanelClipRasterizerState
    {
        private const string ResourceName = "Mirror.NocullClipRasterizerState";

        private static IRasterizerState s_state;
        private static readonly object s_lock = new object();

        /// <summary>Get the cached state, lazily creating it on first
        /// call. Returns <c>null</c> if creation fails (logged); the
        /// next call will retry.</summary>
        public static IRasterizerState Get()
        {
            if (s_state != null) return s_state;
            lock (s_lock)
            {
                if (s_state != null) return s_state;

                var mgr = MyManagers.RasterizerStates;
                if (mgr == null)
                {
                    MyLog.Default.WriteLine(
                        "[Mirror] PanelClipRasterizerState: manager null");
                    return null;
                }

                var desc = default(RasterizerStateDescription);
                desc.FillMode = FillMode.Solid;
                desc.CullMode = CullMode.None;
                desc.IsDepthClipEnabled = true;

                try
                {
                    s_state = mgr.CreateResource(ResourceName, ref desc);
                }
                catch (System.Exception ex)
                {
                    MyLog.Default.WriteLine(
                        "[Mirror] PanelClipRasterizerState create failed: " + ex);
                }
            }
            return s_state;
        }
    }
}
