using System;
using VRageMath;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// RAII override of <see cref="MyRender11.m_resolution"/> and
/// <see cref="MyRender11.ViewportResolution"/>. Push before rendering
/// a panel that should render at less than the full main view's
/// resolution; <see cref="Dispose"/> restores the captured originals.
///
/// <para><b>Hazard:</b> mutating <c>m_resolution</c> per panel render
/// causes <c>MyBorrowedRwTextureManager</c> to mint new borrow keys
/// per unique size, and the engine's depth-stencil pool is hard-capped
/// at 16 slots. The caller is responsible for snapping to a small,
/// fixed set of sizes (see <see cref="Pow2Buckets"/>) so the live
/// working set across the engine's 16-frame retention window stays
/// comfortably under that cap.</para>
///
/// <para>When the requested size equals the current resolution this
/// is a no-op — no globals are touched, no restore is needed.</para>
/// </summary>
internal readonly struct RenderResolutionGuard : IDisposable
{
    private readonly Vector2I _prevResolution;
    private readonly Vector2I _prevViewportResolution;
    private readonly bool     _applied;

    private RenderResolutionGuard(Vector2I prevRes, Vector2I prevVp, bool applied)
    {
        _prevResolution         = prevRes;
        _prevViewportResolution = prevVp;
        _applied                = applied;
    }

    /// <summary>Override <see cref="MyRender11.m_resolution"/> and
    /// <see cref="MyRender11.ViewportResolution"/> to exact pixel
    /// dimensions. Caller is responsible for snapping to a bucketed
    /// set (see <see cref="Pow2Buckets"/>) to keep the borrow-pool
    /// unique-size count bounded. Returns a guard that restores the
    /// originals on <see cref="Dispose"/>. A no-op when the requested
    /// dimensions match the current resolution.</summary>
    public static RenderResolutionGuard Push(Vector2I size)
    {
        var prevRes = MyRender11.m_resolution;
        var prevVp  = MyRender11.ViewportResolution;

        if (size.X == prevRes.X && size.Y == prevRes.Y)
            return new RenderResolutionGuard(default, default, applied: false);

        // Minimum side length so downstream divides (bloom uses /8 etc.)
        // never collapse to zero.
        int newX = Math.Max(64, size.X);
        int newY = Math.Max(64, size.Y);
        var newRes = new Vector2I(newX, newY);

        MyRender11.m_resolution     = newRes;
        MyRender11.ViewportResolution = newRes;

        return new RenderResolutionGuard(prevRes, prevVp, applied: true);
    }

    public void Dispose()
    {
        if (!_applied) return;
        MyRender11.m_resolution       = _prevResolution;
        MyRender11.ViewportResolution = _prevViewportResolution;
    }
}
