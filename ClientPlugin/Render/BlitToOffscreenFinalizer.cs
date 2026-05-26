using VRage.Render11.RenderContext;
using VRage.Render11.Resources;

namespace ClientPlugin;

/// <summary>
/// Single-surface finalizer: clears the destination and copies the
/// post-process sRGB into it via the affine blit shader with a fixed
/// <see cref="BlitTransform"/>. Designed for single-panel renders
/// (mirror uses <see cref="BlitTransform.XFlip"/>, camera uses
/// <see cref="BlitTransform.Identity"/>).
///
/// <para>Implemented as a <c>readonly struct</c> so the generic
/// <see cref="LightweightPanelPipeline.RenderInto"/> call boxes
/// nothing and the JIT can inline the dispatch.</para>
/// </summary>
internal readonly struct BlitToOffscreenFinalizer : IRenderViewFinalizer
{
    private readonly MirrorShader  _shader;
    private readonly IRtvBindable  _destination;
    private readonly BlitTransform _xform;

    public BlitToOffscreenFinalizer(MirrorShader shader, IRtvBindable destination, in BlitTransform xform)
    {
        _shader = shader;
        _destination = destination;
        _xform = xform;
    }

    public void Run(MyRenderContext rc, IBorrowedCustomTexture postProcessed)
        => _shader.ClearAndCopy(rc, postProcessed.SRgb, _destination, in _xform);
}
