using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="IUnitScorer"/>. Computes three per-group inputs
/// the slot selectors read:
/// <list type="bullet">
///   <item><see cref="RenderUnit.DistSq"/> — closest-member block
///         distance² to the viewer. Slot 0 tiebreak; slot 1+ score
///         denominator.</item>
///   <item><see cref="RenderUnit.CenterFactor"/> — cos⁴ of the angle
///         from player-forward to the nearest point on the group's
///         screen rectangle (or to the anchor for groups without a
///         populated plane). Floored at 0.01. Measures "how directly
///         is the player aiming at this".</item>
///   <item><see cref="RenderUnit.Coverage"/> — fraction of main view's
///         screen area the group's union AABB covers, in [0..1].
///         Measures "how much of the player's view does this fill"
///         — a close big mirror dominates a far small one regardless
///         of where the player is looking.</item>
/// </list>
///
/// <para>The two angular metrics are NOT redundant — a tiny mirror
/// dead-center has low Coverage but high CenterFactor; a huge mirror
/// in peripheral has high Coverage but low CenterFactor. Selectors
/// can use either or both depending on intent.</para>
/// </summary>
internal sealed class UnitScorer : IUnitScorer
{
    /// <summary>Floor for <see cref="RenderUnit.CenterFactor"/>. A
    /// slightly off-axis but freshly-stale group can still beat a
    /// perfectly-centered but recently-rendered one — otherwise
    /// peripheral mirrors would never refresh.</summary>
    private const double CenterFactorFloor = 0.01;

    // Reused scratch buffers for the projected-quad → clipped polygon
    // path. 16 vertices is plenty: a quad clipped against 4 viewport
    // edges yields at most 8 vertices.
    private readonly Vector2D[] _clipBufA = new Vector2D[16];
    private readonly Vector2D[] _clipBufB = new Vector2D[16];

    public int Score(IReadOnlyList<PanelGroup> groups,
                     MatrixD playerWorld,
                     MatrixD viewProjection,
                     RenderUnit[] dest)
    {
        if (groups == null || groups.Count == 0) return 0;

        Vector3D eye     = playerWorld.Translation;
        Vector3D forward = playerWorld.Forward;
        forward.Normalize();

        int written = 0;
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            if (g.Members == null || g.Members.Count == 0) continue;

            if (!TryClosestMemberDistSq(g, eye, out double distSq)) continue;

            double centerFactor = ComputeCenterFactor(g, eye, forward);
            double coverage     = ComputeCoverage(g, in viewProjection,
                                                  _clipBufA, _clipBufB,
                                                  out var c0, out var c1,
                                                  out var c2, out var c3,
                                                  out bool quadValid);
            double lookFactor   = ComputeLookFactor(g, eye, forward);

            if (written >= dest.Length) return written;
            dest[written].Group        = g;
            dest[written].DistSq       = distSq;
            dest[written].CenterFactor = centerFactor;
            dest[written].Coverage     = coverage;
            dest[written].LookFactor   = lookFactor;
            dest[written].NdcC0        = c0;
            dest[written].NdcC1        = c1;
            dest[written].NdcC2        = c2;
            dest[written].NdcC3        = c3;
            dest[written].NdcQuadValid = quadValid;
            if (quadValid)
            {
                double minX = Math.Min(Math.Min(c0.X, c1.X), Math.Min(c2.X, c3.X));
                double maxX = Math.Max(Math.Max(c0.X, c1.X), Math.Max(c2.X, c3.X));
                double minY = Math.Min(Math.Min(c0.Y, c1.Y), Math.Min(c2.Y, c3.Y));
                double maxY = Math.Max(Math.Max(c0.Y, c1.Y), Math.Max(c2.Y, c3.Y));
                dest[written].NdcMin = new Vector2D(minX, minY);
                dest[written].NdcMax = new Vector2D(maxX, maxY);
            }
            written++;
        }
        return written;
    }

    // ── CenterFactor ─────────────────────────────────────────────────

    /// <summary>
    /// cos⁴(angle) between player-forward and the eye→member direction,
    /// taken as the MAX across the group's members. Floored at 0.01.
    /// Per-member rather than per-group so a wide multi-panel group
    /// whose centroid sits well off-axis can still score high when
    /// the player IS aimed at one of its individual panels.
    /// </summary>
    private static double ComputeCenterFactor(PanelGroup g, Vector3D eye, Vector3D forward)
    {
        bool hasPlane = g.Normal.LengthSquared() > 0.5;
        var members = g.Members;

        double best = 0.0;
        for (int i = 0; i < members.Count; i++)
        {
            Vector3D anchor;
            if (hasPlane)
            {
                var m = members[i];
                double uMid = (m.UMin + m.UMax) * 0.5;
                double vMid = (m.VMin + m.VMax) * 0.5;
                anchor = g.Origin + g.BasisU * uMid + g.BasisV * vMid;
            }
            else
            {
                var block = members[i].Surface.Block;
                if (block == null) continue;
                anchor = block.WorldMatrix.Translation;
            }

            Vector3D toMember = anchor - eye;
            double toLen = toMember.Length();
            if (toLen <= 0.001) continue;
            double cosAngle = Vector3D.Dot(toMember / toLen, forward);
            if (cosAngle <= 0) continue;
            double align = cosAngle * cosAngle;
            double cf = align * align;          // cos⁴
            if (cf > best) best = cf;
        }
        return Math.Max(CenterFactorFloor, best);
    }

    // ── LookFactor ───────────────────────────────────────────────────

    /// <summary>
    /// MAX across members of: cos⁴ of the angle from forward to the
    /// closest point on the MEMBER's own rectangle. Per-member rather
    /// than per-group's-union so a wide group doesn't score 1.0
    /// merely because the aim ray lands inside the union's bounding
    /// box (which can include gaps between panels) — the aim has to
    /// actually hit one of the individual panels' rects.
    ///
    /// <para>Returns the floor when the group has no populated plane
    /// (no rectangle to intersect), when the look ray is parallel to
    /// or facing away from the plane, or when the intersection is
    /// behind the eye.</para>
    /// </summary>
    private static double ComputeLookFactor(PanelGroup g, Vector3D eye, Vector3D forward)
    {
        if (g.Normal.LengthSquared() <= 0.5) return CenterFactorFloor;

        // Plane intersection is computed once — every member lives on
        // the group's shared plane.
        double signedDist = Vector3D.Dot(eye - g.Origin, g.Normal);
        double fwdN       = Vector3D.Dot(forward, g.Normal);

        // Ray must point INTO the front of the plane. fwdN < 0 with
        // outward-pointing Normal convention.
        const double FwdNEpsilon = 1e-6;
        if (fwdN >= -FwdNEpsilon) return CenterFactorFloor;

        double t = -signedDist / fwdN;
        if (t <= 0) return CenterFactorFloor;

        Vector3D hit   = eye + forward * t;
        Vector3D delta = hit - g.Origin;
        double   hitU  = Vector3D.Dot(delta, g.BasisU);
        double   hitV  = Vector3D.Dot(delta, g.BasisV);

        var members = g.Members;
        double best = 0.0;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            double dU = Math.Max(0, Math.Max(m.UMin - hitU, hitU - m.UMax));
            double dV = Math.Max(0, Math.Max(m.VMin - hitV, hitV - m.VMax));
            if (dU == 0 && dV == 0) return 1.0;          // aim hit THIS panel

            double offRect  = Math.Sqrt(dU * dU + dV * dV);
            double cosAngle = t / Math.Sqrt(t * t + offRect * offRect);
            double lf = cosAngle * cosAngle;
            lf *= lf;
            if (lf > best) best = lf;
        }
        return Math.Max(CenterFactorFloor, best);
    }

    // ── Coverage ─────────────────────────────────────────────────────

    /// <summary>
    /// Fraction of the main camera's screen that the group's union
    /// AABB actually covers, in [0..1] where 1 = entire screen.
    ///
    /// <para>Pipeline:</para>
    /// <list type="number">
    ///   <item>Project the 4 world-space AABB corners into NDC via
    ///         the main view's ViewProjection. If any corner has
    ///         clip-w &lt;= 0 (behind camera), bail to 0 — proper
    ///         near-plane clipping for partial-behind quads is more
    ///         work than the picker needs.</item>
    ///   <item>Sutherland-Hodgman clip the projected quad against
    ///         the 4 NDC viewport edges (left/right/top/bottom),
    ///         producing the visible-on-screen polygon.</item>
    ///   <item>Shoelace formula on the resulting polygon, normalized
    ///         by NDC viewport area (= 4) to get [0..1].</item>
    /// </list>
    /// </summary>
    private static double ComputeCoverage(
        PanelGroup g, in MatrixD vp, Vector2D[] bufA, Vector2D[] bufB,
        out Vector2D ndcC0, out Vector2D ndcC1,
        out Vector2D ndcC2, out Vector2D ndcC3,
        out bool quadValid)
    {
        ndcC0 = ndcC1 = ndcC2 = ndcC3 = default;
        quadValid = false;

        if (g.Normal.LengthSquared() <= 0.5) return 0;

        // 4 corners of the union AABB in world space, ordered such
        // that they form a proper convex quad (BL→BR→TR→TL by
        // (UMin/UMax, VMin/VMax); CCW when basis is right/up).
        Vector3D w0 = g.Origin + g.BasisU * g.UMin + g.BasisV * g.VMin;
        Vector3D w1 = g.Origin + g.BasisU * g.UMax + g.BasisV * g.VMin;
        Vector3D w2 = g.Origin + g.BasisU * g.UMax + g.BasisV * g.VMax;
        Vector3D w3 = g.Origin + g.BasisU * g.UMin + g.BasisV * g.VMax;

        // Corner-behind-near means the player is CLOSE to the panel
        // (eye penetrates the AABB's extruded near volume). Proper
        // clip-space polygon clipping is more work than we need —
        // semantically the panel is huge on screen, return full
        // coverage and the viewport quad so the panel acts as a
        // full-screen occluder for the panel-vs-panel pass.
        if (!TryToNdc(w0, in vp, out bufA[0].X, out bufA[0].Y)
         || !TryToNdc(w1, in vp, out bufA[1].X, out bufA[1].Y)
         || !TryToNdc(w2, in vp, out bufA[2].X, out bufA[2].Y)
         || !TryToNdc(w3, in vp, out bufA[3].X, out bufA[3].Y))
        {
            // Quad is unreliable — leave corners default and flag
            // invalid. Occlusion pass will skip this unit. Coverage
            // returns 1.0 so the picker still treats the close panel
            // as max-priority.
            quadValid = false;
            return 1.0;
        }
        int n = 4;

        // Capture the projected quad corners before Sutherland-Hodgman
        // clips bufA in place.
        ndcC0 = bufA[0];
        ndcC1 = bufA[1];
        ndcC2 = bufA[2];
        ndcC3 = bufA[3];
        quadValid = true;
        // Note: the orchestrator computes NdcMin/NdcMax from the
        // corners on demand — keeping the work here cheap.

        // Sutherland-Hodgman clip against each NDC viewport edge.
        // Ping-pong between bufA and bufB; final result lands in bufA.
        n = ClipPolygonAgainstEdge(bufA, n, bufB, +1,  0, +1.0);  // x <=  1
        n = ClipPolygonAgainstEdge(bufB, n, bufA, -1,  0, +1.0);  // x >= -1
        n = ClipPolygonAgainstEdge(bufA, n, bufB,  0, +1, +1.0);  // y <=  1
        n = ClipPolygonAgainstEdge(bufB, n, bufA,  0, -1, +1.0);  // y >= -1
        if (n < 3) return 0;

        // Shoelace on the clipped polygon, normalized by NDC area (4).
        double area2 = 0;
        for (int i = 0; i < n; i++)
        {
            var a = bufA[i];
            var b = bufA[(i + 1) % n];
            area2 += a.X * b.Y - b.X * a.Y;
        }
        double area = Math.Abs(area2) * 0.5;
        return area * 0.25;
    }

    /// <summary>
    /// Sutherland-Hodgman clip the polygon in <paramref name="src"/>
    /// (length <paramref name="srcCount"/>) against the half-plane
    /// <c>nx·x + ny·y &lt;= d</c>. Writes the clipped polygon to
    /// <paramref name="dst"/> and returns its vertex count.
    /// </summary>
    private static int ClipPolygonAgainstEdge(
        Vector2D[] src, int srcCount,
        Vector2D[] dst,
        double nx, double ny, double d)
    {
        if (srcCount == 0) return 0;

        int dstCount = 0;
        Vector2D s = src[srcCount - 1];
        double sDot = nx * s.X + ny * s.Y;
        for (int i = 0; i < srcCount; i++)
        {
            Vector2D e = src[i];
            double eDot = nx * e.X + ny * e.Y;
            bool sIn = sDot <= d;
            bool eIn = eDot <= d;
            if (eIn)
            {
                if (!sIn)
                {
                    // s outside, e inside: emit intersection, then e
                    double t = (d - sDot) / (eDot - sDot);
                    dst[dstCount++] = new Vector2D(
                        s.X + t * (e.X - s.X),
                        s.Y + t * (e.Y - s.Y));
                }
                dst[dstCount++] = e;
            }
            else if (sIn)
            {
                // s inside, e outside: emit intersection only
                double t = (d - sDot) / (eDot - sDot);
                dst[dstCount++] = new Vector2D(
                    s.X + t * (e.X - s.X),
                    s.Y + t * (e.Y - s.Y));
            }
            // both outside: emit nothing
            s = e;
            sDot = eDot;
        }
        return dstCount;
    }

    private static bool TryToNdc(Vector3D world, in MatrixD vp,
                                 out double ndcX, out double ndcY)
    {
        double cw = world.X * vp.M14 + world.Y * vp.M24 + world.Z * vp.M34 + vp.M44;
        if (cw <= 1e-3) { ndcX = 0; ndcY = 0; return false; }

        double cx = world.X * vp.M11 + world.Y * vp.M21 + world.Z * vp.M31 + vp.M41;
        double cy = world.X * vp.M12 + world.Y * vp.M22 + world.Z * vp.M32 + vp.M42;
        ndcX = cx / cw;
        ndcY = cy / cw;
        return true;
    }

    // ── DistSq ───────────────────────────────────────────────────────

    /// <summary>Distance² from the eye to the closest member's
    /// SCREEN surface — not the block's center. For groups with a
    /// populated plane (the usual case), uses
    /// <c>g.Origin + BasisU × uMid + BasisV × vMid</c> per member.
    /// For unpopulated planes (failed mesh resolution, no-plane
    /// fallback), falls back to the block's WorldMatrix translation.</summary>
    private static bool TryClosestMemberDistSq(
        PanelGroup g, Vector3D eye, out double minDistSq)
    {
        minDistSq = double.MaxValue;
        var members = g.Members;
        bool hasPlane = g.Normal.LengthSquared() > 0.5;

        for (int i = 0; i < members.Count; i++)
        {
            Vector3D pos;
            if (hasPlane)
            {
                var m = members[i];
                double uMid = (m.UMin + m.UMax) * 0.5;
                double vMid = (m.VMin + m.VMax) * 0.5;
                pos = g.Origin + g.BasisU * uMid + g.BasisV * vMid;
            }
            else
            {
                var block = members[i].Surface.Block;
                if (block == null) continue;
                pos = block.WorldMatrix.Translation;
            }

            double d = (pos - eye).LengthSquared();
            if (d < minDistSq) minDistSq = d;
        }
        return minDistSq != double.MaxValue;
    }
}
