using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Result of <see cref="CameraBlockResolver.TryResolve"/>: the camera
/// block's render-side world matrix (with the same +0.2 m forward
/// offset that <c>MyCameraBlock.GetViewMatrix</c> applies) plus the
/// per-LCD-zoomed vertical FOV in radians.
/// </summary>
internal readonly struct CameraBlockView
{
    /// <summary>Render-side world matrix. Translation is shifted +0.2 m
    /// along the camera's Forward so the rendered scene doesn't clip
    /// through the camera block's own model.</summary>
    public readonly MatrixD World;

    /// <summary>Vertical FOV in radians, post zoom. Clamped to a sane
    /// range by the resolver.</summary>
    public readonly float FovV;

    public CameraBlockView(MatrixD world, float fovV) { World = world; FovV = fovV; }
}
