using VRage.Render11.RenderContext;
using VRage.Render11.Resources;

namespace ClientPlugin;

/// <summary>
/// Callback contract for <see cref="LightweightPanelPipeline.RenderInto"/>.
/// The pipeline runs the cull → gbuffer → lighting → tonemap → billboards
/// → fxaa sequence, then hands the post-process sRGB result to the
/// finalizer for the final blit step.
///
/// <para><b>Why generic-struct instead of <c>Action</c>?</b> A
/// closure-capturing lambda allocates a fresh delegate object every
/// call. The pipeline runs once per picked surface per frame; even one
/// allocation per panel × N panels per frame × 60 fps is unnecessary
/// garbage. Implementing this as a struct passed to a generic method
/// (<c>RenderInto&lt;T&gt;(in T)</c> with <c>where T : struct, IRenderViewFinalizer</c>)
/// lets the JIT devirtualize and inline the call, zero allocation.</para>
/// </summary>
internal interface IRenderViewFinalizer
{
    /// <summary>Final stage of a panel render. The pipeline has produced
    /// <paramref name="postProcessed"/> (the tonemapped sRGB result) and
    /// hands it to the finalizer to blit/copy/transform into the LCD's
    /// own offscreen.</summary>
    void Run(MyRenderContext rc, IBorrowedCustomTexture postProcessed);
}
