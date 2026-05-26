using System;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Snaps a pixel dimension up to the nearest entry in a fixed list of
/// power-of-2 sizes <c>{128, 256, 512, 1024}</c>. Used to bucket the
/// panel-render viewport size to the LCD's offscreen RT size so the
/// final blit is always a pure downsample (or 1:1 copy) — never an
/// upscale. Upscale was the source of the "mirror FOV drift" reported
/// at small buckets: at <c>vp 0.25</c> the source was 480×270 and the
/// dest 512×512, so the bilinear blit upscaled by 1.06× / 1.90× and
/// smeared pixels in a way that read as a perspective zoom.
///
/// <para>Per-axis: snap W and H independently so non-square LCDs
/// (e.g. 1024×512 widescreen) keep their aspect ratio. Total unique
/// sizes minted into the engine's borrow pool is bounded by
/// <c>Buckets.Length²</c> = 16 — comfortably under the 16-slot depth-
/// stencil ceiling for the typical case where most LCDs share one of
/// these sizes anyway.</para>
/// </summary>
internal static class Pow2Buckets
{
    private static readonly int[] Buckets = { 128, 256, 512, 1024 };

    /// <summary>Smallest bucket ≥ <paramref name="value"/>. Clamped
    /// to the largest bucket if no entry fits.</summary>
    public static int SnapUp(int value)
    {
        if (value <= Buckets[0]) return Buckets[0];
        for (int i = 0; i < Buckets.Length; i++)
            if (Buckets[i] >= value) return Buckets[i];
        return Buckets[Buckets.Length - 1];
    }

    /// <summary>Per-axis snap. Each axis bucketed independently so the
    /// aspect ratio stays close to the destination LCD's aspect.</summary>
    public static Vector2I SnapUp(Vector2I size)
        => new Vector2I(SnapUp(size.X), SnapUp(size.Y));

    /// <summary>Per-axis snap, then clamp each axis to
    /// <paramref name="maxCap"/> (typically the main view's
    /// resolution) so we never inflate above what's currently
    /// allocated for the gbuffer.</summary>
    public static Vector2I SnapUpCapped(Vector2I size, Vector2I maxCap)
    {
        int x = Math.Min(SnapUp(size.X), maxCap.X);
        int y = Math.Min(SnapUp(size.Y), maxCap.Y);
        return new Vector2I(x, y);
    }

    /// <summary>Bucket closest to <paramref name="value"/> by log-2
    /// distance. Used when we want to round to the nearest pow2
    /// instead of snapping up — allows landing on smaller buckets
    /// when the desired pixel count is significantly below the next
    /// bucket up.</summary>
    public static int SnapNearest(int value)
    {
        if (value <= Buckets[0]) return Buckets[0];
        int best = Buckets[Buckets.Length - 1];
        double bestDist = double.PositiveInfinity;
        double v = Math.Log(Math.Max(1, value));
        for (int i = 0; i < Buckets.Length; i++)
        {
            double d = Math.Abs(Math.Log(Buckets[i]) - v);
            if (d < bestDist) { bestDist = d; best = Buckets[i]; }
        }
        return best;
    }

    /// <summary>Per-axis nearest-bucket snap, capped to
    /// <paramref name="maxCap"/>.</summary>
    public static Vector2I SnapNearestCapped(Vector2I size, Vector2I maxCap)
    {
        int x = Math.Min(SnapNearest(size.X), maxCap.X);
        int y = Math.Min(SnapNearest(size.Y), maxCap.Y);
        return new Vector2I(x, y);
    }
}
