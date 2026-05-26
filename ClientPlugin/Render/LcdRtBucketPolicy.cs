using System;
using VRage.Render11.Resources;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Picks the scene-render viewport size for a panel: bucketed to a
/// power-of-2 pixel size, upper-capped at the destination LCD's
/// offscreen RT size, lower-driven by Coverage so distant panels
/// render smaller.
///
/// <para><b>Caps:</b> never render LARGER than the LCD's offscreen RT
/// — pixels beyond the destination just get averaged away in the
/// blit. <b>Floors:</b> a distant panel renders at a smaller bucket,
/// the blit upscales into the LCD offscreen. Upscale blur is the cost
/// of the perf savings at distance.</para>
///
/// <para><b>Coverage → scale:</b>
/// <c>scale = sqrt(Coverage × Oversample)</c>, clamped to <c>[0, 1]</c>.
/// Multiplied against the LCD RT size to get the desired render dims,
/// then each axis snaps to the nearest power-of-2 in
/// <see cref="Pow2Buckets"/>, finally capped at LCD RT.</para>
/// </summary>
internal sealed class LcdRtBucketPolicy
{
    /// <summary>Multiplier on Coverage before sqrt — how much extra
    /// detail vs the panel's on-screen footprint. Lower = aggressively
    /// shrink distant panels; higher = closer to LCD-native always.
    ///
    /// <para>Tuned so a 0.5×0.5m LCD (512×512 RT) renders at native
    /// resolution out to ~5m, then steps down to 256 from 5–15m, and
    /// 128 beyond. Large wall LCDs (1024 RT) stay near native much
    /// further (Coverage scales with physical-size²).</para></summary>
    private const double Oversample = 100.0;

    /// <summary>Floor under <paramref name="lookFactor"/> so true-
    /// peripheral panels still get a usable bucket instead of
    /// collapsing to the minimum. cos⁴ falls off steeply (cos⁴(60°)
    /// ≈ 0.06, cos⁴(75°) ≈ 0.005) — a hard floor keeps the bucket
    /// math reasonable even at extreme angles.</summary>
    private const double MinLookFactor = 0.10;

    /// <summary>Compute the viewport size to render the unit's panel
    /// scene into. <paramref name="lcdSize"/> is the destination LCD's
    /// offscreen RT size. <paramref name="coverage"/> is the unit's
    /// projected-screen-area fraction. <paramref name="lookFactor"/>
    /// is cos⁴ of the angle between player-forward and the closest
    /// point on the panel's screen-projected AABB — 1.0 when looking
    /// directly at the panel, decays toward 0 for peripheral panels.
    /// Multiplied into the bucket math so off-axis panels degrade
    /// faster (hidden by reduced peripheral visual acuity). Result is
    /// always one of the power-of-2 entries in <see cref="Pow2Buckets"/>,
    /// per axis, capped above by <paramref name="lcdSize"/> and above
    /// by <paramref name="mainViewCap"/>.</summary>
    public Vector2I ResolutionFor(Vector2I lcdSize, double coverage, double lookFactor, Vector2I mainViewCap)
    {
        double look = Math.Max(MinLookFactor, lookFactor);
        double scale = coverage <= 0.0
            ? 1.0
            : Math.Min(1.0, Math.Sqrt(coverage * Oversample * look));

        int desiredW = Math.Max(1, (int)(lcdSize.X * scale));
        int desiredH = Math.Max(1, (int)(lcdSize.Y * scale));

        // Snap UP (smallest bucket ≥ desired) so borderline coverages
        // get the higher-res bucket — same bias the original
        // CoverageResolutionPolicy used. Nearest-snap rounded 362 → 256
        // (half-LCD) which was visibly degraded; snap-up rounds 362 →
        // 512 (LCD-native). LCD-size cap still keeps us from rendering
        // larger than the destination.
        var snapped = Pow2Buckets.SnapUpCapped(
            new Vector2I(desiredW, desiredH),
            lcdSize);

        // Belt-and-braces upper cap at the main view's resolution —
        // we can never usefully render larger than the gbuffer that
        // the engine allocated at startup.
        snapped.X = Math.Min(snapped.X, mainViewCap.X);
        snapped.Y = Math.Min(snapped.Y, mainViewCap.Y);
        return snapped;
    }
}
