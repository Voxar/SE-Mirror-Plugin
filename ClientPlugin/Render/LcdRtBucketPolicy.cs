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
    /// shrink distant panels; higher = closer to mainView-native always.
    ///
    /// <para>Now that the bucket scales by mainViewCap (~1920) instead
    /// of lcdSize (~512), the previous Oversample=100 saturates scale=1
    /// for almost any visible panel → bucket always pegs at 1024 → no
    /// LOD step-down. Oversample=5 lets scale=1 (full mainViewCap
    /// bucket) at ~5% screen coverage; below that it steps down
    /// smoothly: ~0.5% coverage → 512 bucket, ~0.05% → 128.</para></summary>
    private const double Oversample = 5.0;

    /// <summary>Floor under <paramref name="lookFactor"/> so true-
    /// peripheral panels still get a usable bucket instead of
    /// collapsing to the minimum. cos⁴ falls off steeply (cos⁴(60°)
    /// ≈ 0.06, cos⁴(75°) ≈ 0.005) — a hard floor keeps the bucket
    /// math reasonable even at extreme angles.</summary>
    private const double MinLookFactor = 0.10;

    /// <summary>Compute the viewport size to render the unit's panel
    /// scene into. <paramref name="lcdSize"/> is the destination LCD's
    /// offscreen RT size (kept for future per-mode tuning; no longer
    /// caps the result). <paramref name="coverage"/> is the unit's
    /// projected-screen-area fraction. <paramref name="lookFactor"/>
    /// is cos⁴ of the angle between player-forward and the closest
    /// point on the panel's screen-projected AABB — 1.0 when looking
    /// directly at the panel, decays toward 0 for peripheral panels.
    /// Result is in main-view pixels, snapped to the
    /// <see cref="Pow2Buckets"/> grid per axis, capped above by
    /// <paramref name="mainViewCap"/>.
    ///
    /// <para><b>What changed:</b> earlier the bucket was capped at
    /// <paramref name="lcdSize"/>, so a close+focused panel rendered
    /// at most LCD-native (e.g. 512×512) even when the main view was
    /// 1920×1080. That defeated the point of "LOD" — a close mirror
    /// should be HIGHER quality than a distant one, not capped at the
    /// LCD's own pixel grid. Result was identical to LOD-off for
    /// distant panels (mainView), but worse than LOD-off for close
    /// ones (lcdSize). The cap is now main view; close+focused panels
    /// supersample up to mainView, distant panels still step down to
    /// 128/256/512 via the bucket grid.</para></summary>
    public Vector2I ResolutionFor(Vector2I lcdSize, double coverage, double lookFactor, Vector2I mainViewCap)
    {
        double look = Math.Max(MinLookFactor, lookFactor);
        double scale = coverage <= 0.0
            ? 1.0
            : Math.Min(1.0, Math.Sqrt(coverage * Oversample * look));

        // Desired size in main-view pixels (not LCD pixels). Scale=1.0
        // means "render at main view native"; smaller scale steps the
        // bucket down.
        int desiredW = Math.Max(1, (int)(mainViewCap.X * scale));
        int desiredH = Math.Max(1, (int)(mainViewCap.Y * scale));

        // Tiers per axis: {128, 256, 512, 1024} from Pow2Buckets, plus
        // a top tier at mainViewCap so close+focused panels can hit
        // full main-view resolution. Without the top tier, Pow2Buckets
        // would cap at 1024 even when desired = 1920 → close panels
        // would never reach native main-view quality.
        int w = (desiredW > 1024)
            ? Math.Min(desiredW, mainViewCap.X)
            : Pow2Buckets.SnapUp(desiredW);
        int h = (desiredH > 1024)
            ? Math.Min(desiredH, mainViewCap.Y)
            : Pow2Buckets.SnapUp(desiredH);

        return new Vector2I(w, h);
    }
}
