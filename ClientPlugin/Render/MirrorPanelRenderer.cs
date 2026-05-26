using System;
using VRage.Game.Entity;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Renders one solo mirror surface. Resolves the LCD's offscreen RT
/// and screen plane, reflects the viewer across the plane via
/// <see cref="MirrorCamera.TryBuildForPanel"/>, applies the panel
/// camera state under a <see cref="MainCameraStateGuard"/>, runs the
/// lightweight pipeline, and blits the result with an X-flip
/// (undoing the projection's X-flip).
/// </summary>
internal sealed class MirrorPanelRenderer : IPanelRenderer
{
    private readonly IPanelRenderPipeline  _pipeline;
    private readonly MirrorShader          _shader;
    private readonly ILcdOffscreenResolver _offscreenResolver;
    private readonly IScreenPlaneResolver  _planeResolver;
    private readonly IActorMatrixSource    _actorMatrix;
    private readonly IMirrorPluginSettings _settings;

    public MirrorPanelRenderer(
        IPanelRenderPipeline  pipeline,
        MirrorShader          shader,
        ILcdOffscreenResolver offscreenResolver,
        IScreenPlaneResolver  planeResolver,
        IActorMatrixSource    actorMatrix,
        IMirrorPluginSettings settings)
    {
        _pipeline          = pipeline          ?? throw new ArgumentNullException(nameof(pipeline));
        _shader            = shader            ?? throw new ArgumentNullException(nameof(shader));
        _offscreenResolver = offscreenResolver ?? throw new ArgumentNullException(nameof(offscreenResolver));
        _planeResolver     = planeResolver     ?? throw new ArgumentNullException(nameof(planeResolver));
        _actorMatrix       = actorMatrix       ?? throw new ArgumentNullException(nameof(actorMatrix));
        _settings          = settings          ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool Render(PanelSurface surface, in PanelRenderContext ctx)
    {
        try
        {
            if (!(surface.Block is MyEntity blockEntity))
            {
                surface.LastFailure = "block not MyEntity";
                return false;
            }

            // 1. Resolve the LCD offscreen RT. (Step 6 will fold this
            //    into PanelSurface.TryGetOffscreen with caching; for
            //    now, we re-resolve every render.)
            if (!_offscreenResolver.TryResolve(surface.Block, surface.SurfaceIdx, out var off)
                || off.Rtv == null)
            {
                surface.LastFailure = "offscreen not ready";
                return false;
            }

            // 2. World matrix from the freshest render actor. This
            //    INCLUDES the mod-side yaw/pitch mesh tilt (MirrorMeshTilt
            //    component writes the tilted local matrix on the
            //    entity), so the plane built from it ends up at exactly
            //    the same world position as the visibly tilted mesh.
            //    Eligible block + tilt = both tilt together; ineligible
            //    or zero tilt = neither tilts.
            MatrixD blockWorld = _actorMatrix.GetFreshestMatrix(blockEntity);

            // 3. Local mesh plane → world plane.
            if (!_planeResolver.TryResolve(blockEntity, off.MaterialName, out var localPlane))
            {
                // Fall back to an axis-aligned guess (Backward/Right/Up
                // from world matrix, stock LCD half-extents) so a
                // not-yet-introspected mesh still produces a sensible
                // reflection rather than skipping the render.
                localPlane = new ScreenPlaneInfo(
                    localCenter: VRageMath.Vector3.Zero,
                    localNormal: -VRageMath.Vector3.Forward,  // = Backward
                    localRight:  VRageMath.Vector3.Right,
                    localUp:     VRageMath.Vector3.Up,
                    halfWidth:   1.25f,
                    halfHeight:  1.25f,
                    doubleSided: false);
            }
            // Bake the per-surface mirror tilt into the world plane at
            // build time (see PlaneTiltHelper). The same helper is used
            // by the group builder and group plane refresher so every
            // downstream consumer sees the same tilted plane.
            var worldPlane = PlaneTiltHelper.BuildTilted(
                in localPlane, in blockWorld, surface.Config, _settings);

            // 4. Build the reflected camera. Skips when viewer is on
            //    (or behind) the plane.
            var eye = ctx.ViewerWorld.Translation;
            if (!MirrorCamera.TryBuildForPanel(in worldPlane, eye, ctx.EffectiveFarPlaneM, out var cam))
            {
                surface.LastFailure = "viewer behind plane";
                return false;
            }

            // 5. Build panel state, swap engine state under a guard,
            //    run the pipeline.
            var panelState = PanelRenderState.ForMirror(
                in ctx.MainState, in cam,
                ctx.EffectiveFarPlaneM, ctx.EffectiveFarFarPlaneM,
                !_settings.DisableShadows);
            var finalizer = new BlitToOffscreenFinalizer(_shader, off.Rtv, BlitTransform.XFlip);

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
            MyLog.Default.WriteLine("[Mirror] MirrorPanelRenderer: " + ex);
            return false;
        }
    }
}
