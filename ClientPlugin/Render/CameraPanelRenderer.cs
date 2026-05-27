using System;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Renders one solo camera-block-mode surface. Looks up the camera
/// block, builds a standard reverse-Z infinite RH perspective with
/// the panel's aspect ratio, applies state, runs the pipeline, and
/// blits the result without any flip (camera projection is not
/// X-flipped — only mirror is).
/// </summary>
internal sealed class CameraPanelRenderer : IPanelRenderer
{
    private readonly IPanelRenderPipeline  _pipeline;
    private readonly MirrorShader          _shader;
    private readonly ILcdOffscreenResolver _offscreenResolver;
    private readonly ICameraBlockResolver  _cameraResolver;
    private readonly IMirrorPluginSettings _settings;

    public CameraPanelRenderer(
        IPanelRenderPipeline  pipeline,
        MirrorShader          shader,
        ILcdOffscreenResolver offscreenResolver,
        ICameraBlockResolver  cameraResolver,
        IMirrorPluginSettings settings)
    {
        _pipeline          = pipeline          ?? throw new ArgumentNullException(nameof(pipeline));
        _shader            = shader            ?? throw new ArgumentNullException(nameof(shader));
        _offscreenResolver = offscreenResolver ?? throw new ArgumentNullException(nameof(offscreenResolver));
        _cameraResolver    = cameraResolver    ?? throw new ArgumentNullException(nameof(cameraResolver));
        _settings          = settings          ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool Render(PanelSurface surface, in PanelRenderContext ctx)
    {
        try
        {
            // (No per-renderer range cull: RangeCull + MaxScreenRenderDistanceCull
            // in the orchestrator's cull chain already drop out-of-range panels
            // before we get here.)
            var config = surface.Config;

            // 1. Resolve LCD offscreen.
            if (!_offscreenResolver.TryResolve(surface.Block, surface.SurfaceIdx, out var off)
                || off.Rtv == null)
            {
                surface.LastFailure = "offscreen not ready";
                return false;
            }

            // 2. Resolve camera block view (world matrix + post-zoom FovV).
            if (!_cameraResolver.TryResolve(config.CameraBlock, config.Zoom,
                                            out var cv, out string camFail))
            {
                surface.LastFailure = camFail ?? "camera lookup failed";
                return false;
            }

            // 3. Build the projections SE will use verbatim. We route
            // cameras through the off-axis sentinel (msg.FOV=0) so SE
            // uses these matrices without rebuilding from
            // ViewportResolution-aspect — that rebuild was the source
            // of the FOV change when toggling DistanceResolutionScale
            // (bucket aspect ≠ main-view aspect → two different
            // projections from the same camera).
            //
            // Tradeoff: SE's ViewFrustumClippedD uses
            // envMatrices.OriginalProjection = msg.ProjectionMatrix
            // for cull-frustum construction (MyRender11.cs:1890). In
            // the off-axis path the render and the cull frustum share
            // this one matrix, so the cull-widen trick the FOV>0 path
            // allowed (widened msg.ProjectionMatrix for culling while
            // SE rebuilt a strict render projection) is no longer
            // available — edge geometry can pop at panel borders.
            var sz = off.Rtv.Size;
            float aspect = sz.Y > 0 ? (float)sz.X / sz.Y : 1f;
            const float nearPlane = 0.1f;

            // FovH derived from FovV + aspect so downstream consumers
            // (em.FovH/em.FovV writeback in CameraMatrices.Apply,
            // skybox, distance heuristics) see a consistent horizontal
            // half-angle.
            float fovH = (float)(2.0 * Math.Atan(Math.Tan(cv.FovV / 2.0) * aspect));

            // Finite-far reverse-Z render projection at LCD aspect.
            Matrix projection = Matrix.CreatePerspectiveFovRhComplementary(
                cv.FovV, aspect, nearPlane, ctx.EffectiveFarPlaneM);
            // Infinite-far at LCD aspect for skybox (SE reads this as
            // envMatrices.OriginalProjectionFar / projectionForSkybox
            // in the off-axis path, MyRender11.cs:1862, 1876).
            Matrix projectionInfiniteFar = Matrix.CreatePerspectiveFovRhInfiniteComplementary(
                cv.FovV, aspect, nearPlane);

            // 4. Build panel state. cv.World is a rigid transform
            // (orthogonal rotation + translation) so we use CreateLookAt
            // — it builds the view via cross-products on the world's
            // Forward/Up, which is both cheaper than a generic 4×4
            // Invert and numerically stable (no determinant divide that
            // could let an orthogonal rotation drift over many frames).
            MatrixD viewD = MatrixD.CreateLookAt(
                cv.World.Translation,
                cv.World.Translation + cv.World.Forward,
                cv.World.Up);
            var panelState = PanelRenderState.ForCamera(
                in ctx.MainState,
                view: viewD,
                projection: projection,
                projectionInfiniteFar: projectionInfiniteFar,
                cameraPosition: cv.World.Translation,
                fovH: fovH, fovV: cv.FovV,
                farPlaneMeters:    ctx.EffectiveFarPlaneM,
                farFarPlaneMeters: ctx.EffectiveFarFarPlaneM,
                enableShadows:     !_settings.DisableShadows);

            // 5. Swap engine state, render, blit.
            var finalizer = new BlitToOffscreenFinalizer(_shader, off.Rtv, BlitTransform.Identity);
            using (var _ = MainCameraStateGuard.Push(in ctx.MainState, in panelState))
            {
                bool ok = _pipeline.RenderInto(in finalizer);
                if (!ok) surface.LastFailure = "pipeline failed";
                return ok;
            }
        }
        catch (Exception ex)
        {
            surface.LastFailure = ex.GetType().Name + ": " + ex.Message;
            MyLog.Default.WriteLine("[Mirror] CameraPanelRenderer: " + ex);
            return false;
        }
    }
}
