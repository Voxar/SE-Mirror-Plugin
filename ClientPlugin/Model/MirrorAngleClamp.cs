namespace ClientPlugin;

/// <summary>
/// Single source of truth for the per-LCD yaw/pitch cap so callers
/// that need to clamp a slider value all agree on the bound.
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
