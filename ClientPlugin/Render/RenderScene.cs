using System;
using SharpDX.Mathematics.Interop;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin;

/// <summary>
/// Per-panel scene render: cull → gbuffer → lighting → tonemap →
/// billboards → fxaa → finalizer. Closely follows SE's own
/// <c>MyRender11.DrawGameScene_Patch1</c> body (and
/// SE-CameraLCD-Remastered's <c>CameraViewRenderer.Draw</c>) — same
/// stage order, same engine entry points — minus the chunks that don't
/// apply to a panel render (full DRS path, screenshot path, the bulk
/// of post-process effects).
///
/// <para>The finalizer is generic-struct-callback rather than a
/// <see cref="Action"/>: <see cref="LightweightPanelPipeline"/>-style
/// invocation with <c>in</c>-passed struct lets the JIT devirtualize
/// the call and avoid a per-call closure allocation.</para>
/// </summary>
internal sealed class RenderScene : IPanelRenderPipeline
{
    private readonly IFirstPersonHeadFix _headFix;

    public RenderScene(IFirstPersonHeadFix headFix)
    {
        _headFix = headFix ?? throw new ArgumentNullException(nameof(headFix));
    }

    // ── Engine accessors (shorthand) ─────────────────────────────────

    private static MyRenderContext        RC             => MyRender11.RC;
    private static ref MyRenderSettings   Settings       => ref MyRender11.Settings;
    private static ref MyPostprocessSettings Postprocess => ref MyRender11.Postprocess;
    private static MyRenderDebugOverrides DebugOverrides => MyRender11.DebugOverrides;
    private static Vector2I               ResolutionI    => MyRender11.ResolutionI;

    // ── Pipeline ─────────────────────────────────────────────────────

    /// <summary>Run the per-panel pipeline and hand the post-processed
    /// result to <paramref name="finalizer"/>. Catches and logs
    /// exceptions; never propagates to the engine.</summary>
    public bool RenderInto<TFinalizer>(in TFinalizer finalizer)
        where TFinalizer : struct, IRenderViewFinalizer
    {
        IBorrowedRtvTexture    bloom    = null;
        IBorrowedCustomTexture postpp   = null;
        IBorrowedRtvTexture    debugHistogram = null;
        bool ok = false;

        using (var scope = PanelRenderScope.Enter())
        {
            // `scope` exists for its disposal side effect; suppress the
            // "unused" warning explicitly.
            _ = scope;

            try
            {
                // FPV head fix — must happen before the scheduler so
                // the cull pass reads the already-cleared skip flags.
                _headFix.ApplyDuringRender();

                PrepareGameScene();
                RC.ClearState();

                if (MyStereoRender.Enable && MyStereoRender.EnableUsingStencilMask)
                    MyStereoStencilMask.Draw();

                MyManagers.RenderScheduler.Init();
                MyManagers.RenderScheduler.Execute();
                MyManagers.RenderScheduler.Done();

                MyManagers.Ansel?.MarkHdrBufferFinished();

                RC.PixelShader.SetSamplers(0, MySamplerStateManager.StandardSamplers);

                if (Postprocess.EnableEyeAdaptation)
                    MyEyeAdaptation.Run(RC, MyGBuffer.Main.LBuffer, false, out debugHistogram);
                else
                    MyEyeAdaptation.ConstantExposure(RC);

                if (Settings.DisplayHdrIntensity)
                    MyHdrDebugTools.DisplayHdrIntensity(RC, MyGBuffer.Main.LBuffer);

                if (DebugOverrides.Postprocessing && DebugOverrides.Bloom && Postprocess.BloomEnabled)
                {
                    bloom = MyModernBloom.Run(
                        RC,
                        MyGBuffer.Main.LBuffer,
                        MyGBuffer.Main.GBuffer2,
                        MyGBuffer.Main.ResolvedDepthStencil.SrvDepth,
                        MyEyeAdaptation.GetExposure());
                }
                else
                {
                    bloom = MyManagers.RwTexturesPool.BorrowRtv(
                        "bloom_EightScreenUavHDR",
                        ResolutionI.X / 8, ResolutionI.Y / 8,
                        MyGBuffer.LBufferFormat);
                    RC.ClearRtv(bloom, default(RawColor4));
                }

                bool fxaaEnabled = MyRender11.FxaaEnabled;
                postpp = MyToneMapping.Run(
                    MyGBuffer.Main.LBuffer,
                    MyEyeAdaptation.GetExposure(),
                    bloom,
                    Postprocess.EnableTonemapping
                        && DebugOverrides.Postprocessing
                        && DebugOverrides.Tonemapping,
                    Postprocess.DirtTexture,
                    fxaaEnabled);
                bloom.Release();
                bloom = null;

                // Highlight outline: deliberately skipped for panel
                // renders — outline-by-stencil from the panel eye would
                // bleed unwanted highlights into the LCD content.
                // (Left as commented reference for the engine's
                // equivalent in main view's DrawGameScene.)
                //
                // if (MyHighlight.HasHighlights && !MyManagers.Ansel.IsSessionRunning)
                //     MyHighlight.Run(RC, postpp.Linear, null);

                if (Settings.DrawBillboards && Settings.DrawBillboardsLDR)
                    MyBillboardRenderer.RenderLDR(
                        RC, MyGBuffer.Main.ResolvedDepthStencil.SrvDepth, postpp.SRgb);

                if (fxaaEnabled)
                {
                    var fxaa = MyManagers.RwTexturesPool.BorrowCustom("MyRender11.FXAA.Rgb8");
                    MyFXAA.Run(RC, fxaa.Linear, postpp.Linear);
                    postpp.Release();
                    postpp = fxaa;
                }

                // Chromatic aberration / vignette: deliberately skipped
                // (camera/mirror feeds shouldn't carry main view's
                // lens-style effects). Left as engine-reference comment.
                //
                // if (Postprocess.Data.ChromaticFactor != 0f || Postprocess.Data.VignetteStart != 0f) { ... }

                if (Settings.DrawBillboards && Settings.DrawBillboardsPostPP)
                    MyBillboardRenderer.RenderPostPP(
                        RC, MyGBuffer.Main.ResolvedDepthStencil.SrvDepth, postpp.SRgb);

                // Caller-owned final step: copies / blits / fan-outs
                // the post-processed result into the LCD offscreen(s).
                finalizer.Run(RC, postpp);

                ok = true;
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLine("[Mirror] RenderScene: " + ex);
            }
            finally
            {
                // Defensive null-handling: any of these may be null if
                // the corresponding earlier stage threw before assigning.
                debugHistogram?.Release();
                bloom?.Release();
                postpp?.Release();
                MyManagers.Cull.OnFrameEnd();
            }
        }
        return ok;
    }

    /// <summary>Frame-start work the engine does in its own
    /// DrawGameScene: refresh frame constants + voxel material
    /// constants so the GPU sees the panel camera matrices we set up
    /// before getting here.</summary>
    private static void PrepareGameScene()
    {
        // MyManagers.EnvironmentProbe.UpdateProbe();   // bypassed by Patch_MyEnvironmentProbe
        MyCommon.UpdateFrameConstants();
        MyCommon.VoxelMaterialsConstants.FeedGPU();
        // MyOffscreenRenderer.Render();                 // engine handles this in main pass
    }
}
