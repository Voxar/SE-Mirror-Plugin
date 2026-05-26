namespace ClientPlugin;

/// <summary>
/// Single source of truth for the per-LCD mirror yaw/pitch cap so
/// the render-only mesh tilt in <see cref="ModelTiltApplier"/> always
/// agrees with whatever future caller wants to clamp a slider value.
/// </summary>
internal static class MirrorAngleClamp
{
    /// <summary>Plugin-wide hard upper bound (degrees).</summary>
    public const float HardMaxDeg = 45f;

    /// <summary>Clamp a slider value to ±<paramref name="capDeg"/>.</summary>
    public static float Clamp(float angleDeg, float capDeg)
        => angleDeg < -capDeg ? -capDeg
         : (angleDeg > capDeg ?  capDeg : angleDeg);
}
