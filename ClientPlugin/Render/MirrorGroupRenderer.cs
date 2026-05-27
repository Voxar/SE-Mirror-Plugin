using System;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Renders one multi-member coplanar mirror group. Builds a single
/// off-axis reflected camera covering the group's union AABB, runs
/// the pipeline once, then fans out a per-member sub-rect blit so
/// each LCD gets its own slice of the shared post-process result.
/// </summary>
internal sealed class MirrorGroupRenderer : IMirrorGroupRenderer
{
    private readonly IPanelRenderPipeline  _pipeline;
    private readonly MirrorShader          _shader;
    private readonly ILcdOffscreenResolver _offscreenResolver;
    private readonly IMirrorPluginSettings _settings;

    // Pre-allocated scratch array for resolved offscreens. Sized up
    // on demand; reused every render to avoid per-frame allocations.
    // Hot path is render-thread-only so no locking.
    private IRtvBindable[] _scratchOffscreens = new IRtvBindable[8];

    public MirrorGroupRenderer(
        IPanelRenderPipeline  pipeline,
        MirrorShader          shader,
        ILcdOffscreenResolver offscreenResolver,
        IMirrorPluginSettings settings)
    {
        _pipeline          = pipeline          ?? throw new ArgumentNullException(nameof(pipeline));
        _shader            = shader            ?? throw new ArgumentNullException(nameof(shader));
        _offscreenResolver = offscreenResolver ?? throw new ArgumentNullException(nameof(offscreenResolver));
        _settings          = settings          ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool Render(PanelGroup group, in PanelRenderContext ctx)
    {
        if (group == null) return false;
        var members = group.Members;
        if (members == null || members.Count == 0) return false;

        try
        {
            // Resize scratch if needed (rare; only on groups bigger
            // than any prior batch saw).
            if (_scratchOffscreens.Length < members.Count)
                _scratchOffscreens = new IRtvBindable[members.Count];

            // Resolve every member's offscreen up front. Null entries
            // are skipped at blit time but don't abort the group; if
            // ALL members fail we bail entirely.
            int resolvedCount = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var s = members[i].Surface;
                if (_offscreenResolver.TryResolve(s.Block, s.SurfaceIdx, out var info)
                    && info.Rtv != null)
                {
                    _scratchOffscreens[i] = info.Rtv;
                    resolvedCount++;
                }
                else
                {
                    _scratchOffscreens[i] = null;
                }
            }
            if (resolvedCount == 0) return false;

            // Build the group's mirror camera.
            var eye = ctx.ViewerWorld.Translation;
            if (!MirrorCamera.TryBuildForGroup(group, eye, ctx.EffectiveFarPlaneM, out var cam))
                return false;

            // Player-in-reflection check, stamped on every member so
            // the scheduler can read the lead surface for the small
            // priority bonus next frame. One frustum test per group.
            {
                MatrixD viewProj = cam.View * (MatrixD)cam.Projection;
                var frustum = new BoundingFrustumD(viewProj);
                bool playerInFrustum = frustum.Contains(eye) != ContainmentType.Disjoint;
                for (int i = 0; i < members.Count; i++)
                    members[i].Surface.PlayerInReflectionLastRender = playerInFrustum;
            }

            double unionW = group.UMax - group.UMin;
            double unionH = group.VMax - group.VMin;
            if (unionW <= 0 || unionH <= 0) return false;

            var panelState = PanelRenderState.ForMirror(
                in ctx.MainState, in cam,
                ctx.EffectiveFarPlaneM, ctx.EffectiveFarFarPlaneM,
                !_settings.DisableShadows);

            var finalizer = new GroupFanoutFinalizer(
                _shader, group, _scratchOffscreens, unionW, unionH);

            bool ok;
            using (var _ = MainCameraStateGuard.Push(in ctx.MainState, in panelState))
            {
                ok = _pipeline.RenderInto(in finalizer);
            }

            // Don't hold references in the scratch array past the
            // render — would keep RTVs alive longer than needed.
            for (int i = 0; i < members.Count; i++) _scratchOffscreens[i] = null;
            return ok;
        }
        catch (Exception ex)
        {
            MyLog.Default.WriteLine("[Mirror] MirrorGroupRenderer: " + ex);
            // Clear scratch on exception too.
            for (int i = 0; i < members.Count && i < _scratchOffscreens.Length; i++)
                _scratchOffscreens[i] = null;
            return false;
        }
    }
}
