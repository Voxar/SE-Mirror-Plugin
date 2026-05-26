namespace ClientPlugin;

/// <summary>
/// Affine UV transform fed to <see cref="IAffineBlit"/>'s shader. The
/// shader samples the source via
/// <c>src_uv = Origin + dst.x · AxisX + dst.y · AxisY</c> where
/// <c>dst.xy ∈ [0..1]²</c>. Handles X-flip (mirror compensation),
/// axis-aligned sub-rect (group fan-out), and 90/180/270° rotations
/// uniformly.
///
/// <para>The two named factories cover the common cases; build
/// arbitrary transforms manually for grouped mirror member blits.</para>
/// </summary>
internal readonly struct BlitTransform
{
    public readonly float OriginU, OriginV;
    public readonly float AxisXU,  AxisXV;
    public readonly float AxisYU,  AxisYV;

    public BlitTransform(
        float originU, float originV,
        float axisXU, float axisXV,
        float axisYU, float axisYV)
    {
        OriginU = originU; OriginV = originV;
        AxisXU  = axisXU;  AxisXV  = axisXV;
        AxisYU  = axisYU;  AxisYV  = axisYV;
    }

    /// <summary>Identity blit: dst.xy → src.xy (no flip, no rotation,
    /// full-source). Used for camera-mode panel finalization.</summary>
    public static BlitTransform Identity =>
        new(0f, 0f,
            1f, 0f,
            0f, 1f);

    /// <summary>X-flip full-source blit: dst.x → 1-src.u, dst.y → src.v.
    /// Used for single-panel mirror finalization to undo the X-flip
    /// baked into the off-axis projection.</summary>
    public static BlitTransform XFlip =>
        new(1f, 0f,
           -1f, 0f,
            0f, 1f);
}
