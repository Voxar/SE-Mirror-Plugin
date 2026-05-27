using System;
using System.Collections.Generic;
using VRage.Game.Entity;
using VRageMath;
using VRageRender;     // MyRender11.ResolutionI

namespace ClientPlugin;

/// <summary>
/// Default <see cref="PanelGroupBuilder"/>: rebuilds groups only when
/// the surface registry version changes. Walks the current snapshot
/// once, places each surface in the first compatible coplanar same-
/// grid mirror group or creates a new solo group otherwise.
///
/// <para>Two surfaces share a group iff:</para>
/// <list type="bullet">
///   <item>both are <see cref="PanelMode.Mirror"/>;</item>
///   <item>same parent grid (no cross-grid merges);</item>
///   <item>same plane normal (quantized to ~1 mm tolerance);</item>
///   <item>same signed-distance to origin (within 30 cm — accommodates
///         LCD screen-depth variation between block types on the same
///         flat surface);</item>
///   <item>basis vectors aligned at a 90° multiple (0/90/180/270°);</item>
///   <item>the tentative merged RT wouldn't exceed the engine's
///         main viewport resolution on either axis — the cap that
///         comes for free since that RT is already allocated for
///         the primary render. Above that, the merge would force
///         each member to sample a sub-resolution of the union,
///         producing visible blur.</item>
/// </list>
/// </summary>
internal sealed class PanelGroupBuilder
{
    private const double QuantizeUnitsPerMeter = 1024.0;   // ~1 mm tolerance on normals
    private const double DistanceToleranceM    = 0.05;     // 10 cm between coplanar walls
    private const int    ApproxTargetPx        = 512;       // typical native LCD pixel dim
    private const double TouchingTolerance     = 0.10;     // edges within 10 cm = "touching"

    private readonly ScreenPlaneResolver  _planeResolver;
    private readonly ActorMatrixSource    _actorMatrix;
    private readonly IMirrorPluginSettings _settings;

    private readonly List<PanelGroup> _groups = new();
    private int _lastSeenVersion = -1;

    // Per-candidate scratch: indices of every existing group that the
    // candidate is touching (gap ≤ TouchingTolerance on each axis).
    // When this collects more than one entry the candidate is a
    // physical bridge between previously-disjoint sub-walls; the
    // outer loop dissolves those groups together so the bridged set
    // ends up as one group instead of two-plus-bridge.
    private readonly List<int> _touchedScratch = new();

    // Sticky flag: the last Rebuild encountered at least one mirror
    // surface whose plane wouldn't resolve (block in a transient
    // state — Render component / screen-areas / matrix not ready).
    // While set, GetGroups forces a fresh Rebuild every batch so the
    // panel gets a real plane-bearing group once the transient
    // window passes — coplanar neighbours then merge with it. Mirror
    // panels are SKIPPED outright (no group added) when their plane
    // doesn't resolve, so the merge slot is left open for them to
    // claim properly on retry. Camera panels DO still get a no-plane
    // solo group as a fallback because their renderer projects from
    // the camera block, not the LCD plane — they can still render
    // without it.
    private bool _hasUnresolvedMirrorPlanes;

    public PanelGroupBuilder(
        ScreenPlaneResolver  planeResolver,
        ActorMatrixSource    actorMatrix,
        IMirrorPluginSettings settings)
    {
        _planeResolver = planeResolver ?? throw new ArgumentNullException(nameof(planeResolver));
        _actorMatrix   = actorMatrix   ?? throw new ArgumentNullException(nameof(actorMatrix));
        _settings      = settings      ?? throw new ArgumentNullException(nameof(settings));
    }

    public IReadOnlyList<PanelGroup> GetGroups(SurfaceRegistry registry)
    {
        int currentVersion = registry.Version;
        if (currentVersion == _lastSeenVersion && !_hasUnresolvedMirrorPlanes)
            return _groups;

        Rebuild(registry);
        _lastSeenVersion = currentVersion;
        return _groups;
    }

    public void InvalidateCache() => _lastSeenVersion = -1;

    // ── Rebuild ──────────────────────────────────────────────────────

    private void Rebuild(SurfaceRegistry registry)
    {
        // Clear and break PanelSurface back-references so a surface
        // doesn't keep pointing at a stale group that we're about to
        // drop on the floor.
        for (int i = 0; i < _groups.Count; i++)
        {
            var members = _groups[i].Members;
            for (int j = 0; j < members.Count; j++)
                members[j].Surface.Group = null;
        }
        _groups.Clear();

        _hasUnresolvedMirrorPlanes = false;

        var surfaces = registry.SnapshotForRender();
        if (surfaces == null) return;

        for (int si = 0; si < surfaces.Length; si++)
        {
            var surface = surfaces[si];
            if (surface == null || surface.Block == null) continue;

            // Resolve the LCD's tilted screen plane for EVERY panel —
            // mirror and camera modes both need it for slot-0 scoring
            // (Coverage / LookFactor are computed from the plane's
            // world-space rect). Per-surface mirror yaw/pitch is baked
            // in here so the rest of the grouping math (normal-match,
            // basis-rotation, RT budget) operates on the tilted plane.
            // The mirror-vs-camera distinction only matters for the
            // merging step below; camera-mode panels never merge.
            bool hasPlane = TryReadScreenPlane(surface, out var worldPlane);
            long gridId   = TryGetGridEntityId(surface);

            // Mirror panels REQUIRE a resolved plane. Without one,
            // the panel would either occupy a no-plane solo slot
            // (preventing a future coplanar neighbour from merging
            // here, since the merge check compares normals to zero
            // and rejects), or render with a degenerate projection.
            // Skip it entirely this batch and flag so GetGroups
            // retries Rebuild next batch — by then the block's
            // transient initialisation should have settled and the
            // resolver will return a real plane.
            if (!hasPlane && surface.Mode == PanelMode.Mirror)
            {
                _hasUnresolvedMirrorPlanes = true;
                continue;
            }

            // Merge eligibility: must be mirror mode, must have a
            // resolved plane, must have a grid. The mirror-angle tilt
            // is already baked into worldPlane by PlaneTiltHelper, so
            // two mirrors with the SAME tilt have the same world normal
            // and group naturally; tilt-vs-untilt have different
            // normals and stay separate, no special-case needed here.
            bool canMerge =
                hasPlane
                && surface.Mode == PanelMode.Mirror
                && gridId != 0L;

            if (!canMerge)
            {
                // hasPlane=false here can only be a camera-mode
                // surface (mirror without plane was already skipped
                // above). Camera renderer doesn't need the LCD plane,
                // so we still produce a renderable group.
                _groups.Add(hasPlane
                    ? NewSoloGroupWithPlane(surface, gridId, in worldPlane)
                    : MakeSoloGroupNoPlane(surface));
                continue;
            }

            // Look for an existing mirror-mode group with the same
            // grid + quantized plane that this surface can join.
            // TryFindGroupForMerge populates _touchedScratch with the
            // indices of every group the candidate is TOUCHING (gap ≤
            // TouchingTolerance). If multiple, dissolve them into one
            // first — the candidate bridges them.
            int matched = TryFindGroupForMerge(surface, gridId, in worldPlane, out int rotation);
            if (matched >= 0)
            {
                if (_touchedScratch.Count > 1)
                    matched = DissolveTouchedGroups(matched);
                AddMember(_groups[matched], surface, in worldPlane, rotation);
            }
            else
            {
                _groups.Add(NewSoloGroupWithPlane(surface, gridId, in worldPlane));
            }
        }

    }

    // ── Merge eligibility ────────────────────────────────────────────

    /// <summary>Returns index of the BEST compatible existing group,
    /// or -1 if none. "Best" = the group whose nearest member's screen
    /// center is closest in world space to <paramref name="plane"/>'s
    /// center. This prioritizes merging a panel with its physical
    /// neighbours rather than the first match the iteration happens
    /// to find — without it, a panel on a long wall could end up in
    /// a group with distant mirrors purely because of snapshot
    /// iteration order, leaving its edge-to-edge neighbours stranded
    /// in their own solos.
    ///
    /// <para>Distance is the only secondary criterion. Coplanarity
    /// (normal + signed distance) is still required, and the RT-size
    /// budget still caps how large any single group can grow — so
    /// far-away coplanar panels can still merge when the union fits.
    /// Sets <paramref name="rotation"/> to the merged member's
    /// rotation (0/1/2/3) of the chosen group.</para>
    /// </summary>
    private int TryFindGroupForMerge(
        PanelSurface candidate, long gridId, in WorldScreenPlane plane, out int rotation)
    {
        rotation = 0;

        double q = QuantizeUnitsPerMeter;
        int nx = (int)Math.Round(plane.Normal.X * q);
        int ny = (int)Math.Round(plane.Normal.Y * q);
        int nz = (int)Math.Round(plane.Normal.Z * q);
        int ux = (int)Math.Round(plane.Right.X  * q);
        int uy = (int)Math.Round(plane.Right.Y  * q);
        int uz = (int)Math.Round(plane.Right.Z  * q);
        double signedDist = Vector3D.Dot(plane.Center, plane.Normal);

        int    bestIdx     = -1;
        int    bestRot     = 0;
        double bestDistSq  = double.PositiveInfinity;
        _touchedScratch.Clear();

        for (int gi = 0; gi < _groups.Count; gi++)
        {
            var g = _groups[gi];
            if (g.Members.Count == 0) continue;
            if (g.Members[0].Surface.Mode != PanelMode.Mirror) continue;
            if (g.GridEntityId != gridId)                      continue;
            if ((int)Math.Round(g.Normal.X * q) != nx
             || (int)Math.Round(g.Normal.Y * q) != ny
             || (int)Math.Round(g.Normal.Z * q) != nz)         continue;

            // In-plane rotation: candidate.basisU vs group.basisU/V →
            //   0 = +group.basisU (same orientation)
            //   1 = +group.basisV (90° CCW)
            //   2 = -group.basisU (upside-down / 180°)
            //   3 = -group.basisV (270° CCW)
            int cux = (int)Math.Round(g.BasisU.X * q);
            int cuy = (int)Math.Round(g.BasisU.Y * q);
            int cuz = (int)Math.Round(g.BasisU.Z * q);
            int cvx = (int)Math.Round(g.BasisV.X * q);
            int cvy = (int)Math.Round(g.BasisV.Y * q);
            int cvz = (int)Math.Round(g.BasisV.Z * q);

            int rot;
            if      (ux ==  cux && uy ==  cuy && uz ==  cuz) rot = 0;
            else if (ux ==  cvx && uy ==  cvy && uz ==  cvz) rot = 1;
            else if (ux == -cux && uy == -cuy && uz == -cuz) rot = 2;
            else if (ux == -cvx && uy == -cvy && uz == -cvz) rot = 3;
            else continue;

            double candidateDist = Vector3D.Dot(g.Origin, g.Normal);
            if (Math.Abs(candidateDist - signedDist) > DistanceToleranceM) continue;

            // RT-size guard. The proportional render target (each
            // member targeting 512 of the union's px width) must fit
            // the main view's RT, OR the candidate is literally
            // touching an existing member — in which case the budget
            // is bypassed. Bypassed merges still cap implicitly at
            // the main RT because that's where the render runs; the
            // panels just get fewer effective pixels each. Mirror
            // walls opt into that tradeoff by being built touching.
            bool touchingOverride = IsTouchingAnyMember(g, in plane, rot);
            if (!touchingOverride && !FitsRtSizeBudget(g, in plane, rot)) continue;

            // Adjacency guard. Don't merge unless the candidate's
            // (U,V) AABB sits within one-of-its-own-dimensions of an
            // existing member's AABB. Without this, an unrelated
            // coplanar same-grid panel anywhere on the build could
            // get absorbed into a group purely because RT-budget
            // math fits — even with empty space in between that
            // wastes a chunk of the merged render's pixels. A
            // candidate that fails this stays solo; subsequent
            // candidates may fill the gap and let it merge in a
            // future rebuild.
            if (!IsAdjacentToGroup(g, in plane, rot)) continue;

            // Record whether the candidate is literally touching this
            // group — used by the outer loop's bridge-dissolve step.
            if (IsTouchingAnyMember(g, in plane, rot)) _touchedScratch.Add(gi);

            // All hard checks passed — this group is a valid merge
            // target. Score it by world-space distance to the
            // candidate's screen center, taken across all the group's
            // current members. Closest wins.
            double dSq = NearestMemberDistSq(g, plane.Center);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestIdx    = gi;
                bestRot    = rot;
            }
        }

        rotation = bestRot;
        return bestIdx;
    }

    /// <summary>
    /// True iff the candidate's (U,V) AABB is within
    /// <see cref="TouchingTolerance"/> of an existing member's AABB
    /// on each axis. Tighter than <see cref="IsAdjacentToGroup"/> —
    /// strictly "touching", not "one panel away". Touching panels
    /// bypass the proportional RT-size budget so walls of contiguous
    /// mirrors merge into one group rather than being split into
    /// chunks.
    /// </summary>
    private static bool IsTouchingAnyMember(
        PanelGroup g, in WorldScreenPlane plane, int rotation)
    {
        Vector3D delta = plane.Center - g.Origin;
        double cu = Vector3D.Dot(delta, g.BasisU);
        double cv = Vector3D.Dot(delta, g.BasisV);
        double cuExt = (rotation == 1 || rotation == 3) ? plane.HalfHeight : plane.HalfWidth;
        double cvExt = (rotation == 1 || rotation == 3) ? plane.HalfWidth  : plane.HalfHeight;
        double cuMin = cu - cuExt, cuMax = cu + cuExt;
        double cvMin = cv - cvExt, cvMax = cv + cvExt;

        var members = g.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            var m = members[mi];
            double gU = Math.Max(0, Math.Max(m.UMin - cuMax, cuMin - m.UMax));
            double gV = Math.Max(0, Math.Max(m.VMin - cvMax, cvMin - m.VMax));
            if (gU <= TouchingTolerance && gV <= TouchingTolerance) return true;
        }
        return false;
    }

    /// <summary>
    /// True iff the candidate is physically next to some existing
    /// member of <paramref name="g"/>. "Next to" = the per-axis gap
    /// between the candidate's (U,V) AABB and that member's AABB is
    /// within one half-panel-dimension on each axis. Edge-to-edge
    /// (gap=0) trivially passes. A coplanar-but-isolated panel
    /// across an empty stretch of wall fails — should stay solo
    /// rather than absorb into an unrelated pair just because the
    /// RT-budget math happened to fit.
    /// </summary>
    private static bool IsAdjacentToGroup(
        PanelGroup g, in WorldScreenPlane plane, int rotation)
    {
        Vector3D delta = plane.Center - g.Origin;
        double cu = Vector3D.Dot(delta, g.BasisU);
        double cv = Vector3D.Dot(delta, g.BasisV);
        double cuExt = (rotation == 1 || rotation == 3) ? plane.HalfHeight : plane.HalfWidth;
        double cvExt = (rotation == 1 || rotation == 3) ? plane.HalfWidth  : plane.HalfHeight;
        double cuMin = cu - cuExt, cuMax = cu + cuExt;
        double cvMin = cv - cvExt, cvMax = cv + cvExt;

        // Threshold per axis = candidate's full panel dimension on
        // that axis (= 2 × half-extent). Touching (gap=0) passes;
        // one missing panel between us and a member (gap ≈ panel
        // dimension) still passes — important so that yanking a
        // middle screen out of a 3-in-a-row doesn't immediately
        // shatter the outer two into solos. Two missing panels
        // (gap ≥ 2× dimension) reject — different "zone" of wall.
        double thU = 2.0 * cuExt;
        double thV = 2.0 * cvExt;

        var members = g.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            var m = members[mi];
            double gU = Math.Max(0, Math.Max(m.UMin - cuMax, cuMin - m.UMax));
            double gV = Math.Max(0, Math.Max(m.VMin - cvMax, cvMin - m.VMax));
            if (gU <= thU && gV <= thV) return true;
        }
        return false;
    }

    /// <summary>
    /// When the candidate touches multiple existing groups (entries
    /// in <see cref="_touchedScratch"/>), fold them all into the
    /// group at <paramref name="targetIdx"/> and remove the others
    /// from <c>_groups</c>. Returns the post-removal index of the
    /// kept group, which the caller then merges the candidate into.
    ///
    /// <para>Handles any rotation between src and target — all
    /// groups in <c>_touchedScratch</c> are coplanar with the
    /// candidate (distance tolerance check passed) and therefore
    /// coplanar with each other. <see cref="TransferMembers"/>
    /// reprojects each moved member's rectangle through world space
    /// so any quarter-turn rotation between src and dst basis is
    /// absorbed correctly.</para>
    /// </summary>
    private int DissolveTouchedGroups(int targetIdx)
    {
        // Iterate descending so RemoveAt doesn't shift indices we
        // haven't visited yet. Skip the target itself.
        for (int i = _touchedScratch.Count - 1; i >= 0; i--)
        {
            int gi = _touchedScratch[i];
            if (gi == targetIdx) continue;
            var src = _groups[gi];

            TransferMembers(src, _groups[targetIdx]);
            _groups.RemoveAt(gi);
            if (gi < targetIdx) targetIdx--;
        }
        _touchedScratch.Clear();
        return targetIdx;
    }

    /// <summary>Move every member of <paramref name="src"/> into
    /// <paramref name="dst"/>, reprojecting each member's rectangle
    /// through world space so any quarter-turn rotation between the
    /// two bases is absorbed. Member.Rotation is updated by the
    /// src-to-dst rotation so the fanout finalizer's per-member blit
    /// math stays correct in <paramref name="dst"/>'s frame. Expands
    /// <paramref name="dst"/>'s union AABB to cover the new members.
    /// Caller must guarantee src and dst are coplanar.</summary>
    private static void TransferMembers(PanelGroup src, PanelGroup dst)
    {
        // src→dst rotation index — which dst axis does src.BasisU
        // align with? Same convention as TryFindGroupForMerge:
        //   0 = +dst.BasisU   1 = +dst.BasisV
        //   2 = -dst.BasisU   3 = -dst.BasisV
        int srcToDst;
        double dotU = Vector3D.Dot(src.BasisU, dst.BasisU);
        double dotV = Vector3D.Dot(src.BasisU, dst.BasisV);
        if      (dotU >  0.95) srcToDst = 0;
        else if (dotV >  0.95) srcToDst = 1;
        else if (dotU < -0.95) srcToDst = 2;
        else                   srcToDst = 3;  // dotV < -0.95

        var members = src.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            var m = members[mi];

            // Project the member's 4 world-space corners into dst's
            // basis. Taking the AABB of those 4 points yields the
            // member's rectangle in dst's frame — works for any
            // quarter-turn rotation, where the simple uCenter+halfW
            // formulation would have copied the wrong axis extents.
            Vector3D c0 = src.Origin + m.UMin * src.BasisU + m.VMin * src.BasisV;
            Vector3D c1 = src.Origin + m.UMax * src.BasisU + m.VMin * src.BasisV;
            Vector3D c2 = src.Origin + m.UMax * src.BasisU + m.VMax * src.BasisV;
            Vector3D c3 = src.Origin + m.UMin * src.BasisU + m.VMax * src.BasisV;
            double u0 = Vector3D.Dot(c0 - dst.Origin, dst.BasisU); double v0 = Vector3D.Dot(c0 - dst.Origin, dst.BasisV);
            double u1 = Vector3D.Dot(c1 - dst.Origin, dst.BasisU); double v1 = Vector3D.Dot(c1 - dst.Origin, dst.BasisV);
            double u2 = Vector3D.Dot(c2 - dst.Origin, dst.BasisU); double v2 = Vector3D.Dot(c2 - dst.Origin, dst.BasisV);
            double u3 = Vector3D.Dot(c3 - dst.Origin, dst.BasisU); double v3 = Vector3D.Dot(c3 - dst.Origin, dst.BasisV);
            double newUMin = Math.Min(Math.Min(u0, u1), Math.Min(u2, u3));
            double newUMax = Math.Max(Math.Max(u0, u1), Math.Max(u2, u3));
            double newVMin = Math.Min(Math.Min(v0, v1), Math.Min(v2, v3));
            double newVMax = Math.Max(Math.Max(v0, v1), Math.Max(v2, v3));

            // Member.Rotation tracks its screen orientation relative
            // to its group's basis. After moving src→dst, the new
            // rotation is the old rotation composed with the
            // src→dst rotation.
            int newRotation = (m.Rotation + srcToDst) & 3;

            var moved = new GroupMember(m.Surface,
                uMin: newUMin, uMax: newUMax,
                vMin: newVMin, vMax: newVMax,
                rotation: newRotation);
            dst.Members.Add(moved);
            m.Surface.Group = dst;

            if (moved.UMin < dst.UMin) dst.UMin = moved.UMin;
            if (moved.UMax > dst.UMax) dst.UMax = moved.UMax;
            if (moved.VMin < dst.VMin) dst.VMin = moved.VMin;
            if (moved.VMax > dst.VMax) dst.VMax = moved.VMax;
        }
    }

    /// <summary>World-space squared distance from <paramref name="point"/>
    /// to the closest member's screen center in <paramref name="g"/>.
    /// Member centers are reconstructed from the member's (U,V) AABB
    /// midpoint projected through the group's plane basis.</summary>
    private static double NearestMemberDistSq(PanelGroup g, Vector3D point)
    {
        double best = double.PositiveInfinity;
        var members = g.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            var m = members[mi];
            double uC = 0.5 * (m.UMin + m.UMax);
            double vC = 0.5 * (m.VMin + m.VMax);
            Vector3D memCenter = g.Origin + uC * g.BasisU + vC * g.BasisV;
            double d = (point - memCenter).LengthSquared();
            if (d < best) best = d;
        }
        return best;
    }

    private bool FitsRtSizeBudget(
        PanelGroup g, in WorldScreenPlane newPlane, int rotation)
    {
        // Project candidate center into group's (U,V) plane coords.
        Vector3D delta = newPlane.Center - g.Origin;
        double uC = Vector3D.Dot(delta, g.BasisU);
        double vC = Vector3D.Dot(delta, g.BasisV);
        double uExt = (rotation == 1 || rotation == 3) ? newPlane.HalfHeight : newPlane.HalfWidth;
        double vExt = (rotation == 1 || rotation == 3) ? newPlane.HalfWidth  : newPlane.HalfHeight;
        double uMin = Math.Min(g.UMin, uC - uExt);
        double uMax = Math.Max(g.UMax, uC + uExt);
        double vMin = Math.Min(g.VMin, vC - vExt);
        double vMax = Math.Max(g.VMax, vC + vExt);
        double unionW = uMax - uMin;
        double unionH = vMax - vMin;

        double minW = 2.0 * uExt;
        double minH = 2.0 * vExt;
        for (int mi = 0; mi < g.Members.Count; mi++)
        {
            var m = g.Members[mi];
            double mw = m.UMax - m.UMin;
            double mh = m.VMax - m.VMin;
            if (mw > 0 && mw < minW) minW = mw;
            if (mh > 0 && mh < minH) minH = mh;
        }

        // RT-size cap = engine's main viewport resolution, per axis.
        // That RT is already allocated for the primary render, so
        // matching it is the natural "free" upper bound — anything
        // larger would force per-member sub-resolution sampling and
        // blur the result.
        var res = MyRender11.ResolutionI;
        int rtMaxW = res.X > 0 ? res.X : 1024;
        int rtMaxH = res.Y > 0 ? res.Y : 1024;
        double tentRtW = ApproxTargetPx * unionW / Math.Max(0.001, minW);
        double tentRtH = ApproxTargetPx * unionH / Math.Max(0.001, minH);
        return tentRtW <= rtMaxW && tentRtH <= rtMaxH;
    }

    // ── Construction helpers ─────────────────────────────────────────

    /// <summary>Fallback: a panel whose screen plane couldn't be
    /// resolved (mesh introspection failed, no model loaded yet,
    /// etc.). No plane data on the group — the slot-0 picker's
    /// plane-dependent signals (Coverage, LookFactor) score these as
    /// zero / floor, so they only render via slot-1+ staleness.</summary>
    private PanelGroup MakeSoloGroupNoPlane(PanelSurface surface)
    {
        long gridId = TryGetGridEntityId(surface);
        var g = new PanelGroup(gridId, initialCapacity: 1);
        g.Members.Add(new GroupMember(
            surface,
            uMin: 0, uMax: 0, vMin: 0, vMax: 0,
            rotation: 0));
        surface.Group = g;
        return g;
    }

    /// <summary>A solo group with the panel's screen plane populated.
    /// Used for camera-mode panels (which never merge) and for mirror-
    /// mode panels whose grouping is disabled / has no compatible
    /// neighbours yet (subsequent mirrors may merge into this group
    /// via <see cref="AddMember"/>).</summary>
    private PanelGroup NewSoloGroupWithPlane(
        PanelSurface anchor, long gridId, in WorldScreenPlane plane)
    {
        var g = new PanelGroup(gridId, initialCapacity: 4)
        {
            Origin = plane.Center,
            Normal = plane.Normal,
            BasisU = plane.Right,
            BasisV = plane.Up,
            UMin = -plane.HalfWidth,  UMax = +plane.HalfWidth,
            VMin = -plane.HalfHeight, VMax = +plane.HalfHeight,
        };
        g.Members.Add(new GroupMember(
            anchor,
            uMin: -plane.HalfWidth,  uMax: +plane.HalfWidth,
            vMin: -plane.HalfHeight, vMax: +plane.HalfHeight,
            rotation: 0));
        anchor.Group = g;
        return g;
    }

    private static void AddMember(
        PanelGroup g, PanelSurface surface, in WorldScreenPlane plane, int rotation)
    {
        // Project the new member's center into group (U,V) coords.
        Vector3D delta = plane.Center - g.Origin;
        double uCenter = Vector3D.Dot(delta, g.BasisU);
        double vCenter = Vector3D.Dot(delta, g.BasisV);

        // 90°/270° rotated members have their LCD's halfW span the
        // group's V axis (and halfH spans U axis) — swap extents.
        double uExt = (rotation == 1 || rotation == 3) ? plane.HalfHeight : plane.HalfWidth;
        double vExt = (rotation == 1 || rotation == 3) ? plane.HalfWidth  : plane.HalfHeight;

        var mem = new GroupMember(
            surface,
            uMin: uCenter - uExt, uMax: uCenter + uExt,
            vMin: vCenter - vExt, vMax: vCenter + vExt,
            rotation: rotation);
        g.Members.Add(mem);
        surface.Group = g;

        // Update union AABB.
        if (mem.UMin < g.UMin) g.UMin = mem.UMin;
        if (mem.UMax > g.UMax) g.UMax = mem.UMax;
        if (mem.VMin < g.VMin) g.VMin = mem.VMin;
        if (mem.VMax > g.VMax) g.VMax = mem.VMax;
    }

    // ── Plane / grid lookup ──────────────────────────────────────────

    /// <summary>Resolve an LCD's screen plane in world space. Works
    /// for any panel mode (mirror or camera) that has a resolvable
    /// screen mesh — returns false if the block has no model loaded,
    /// no screen-areas component, or the mesh introspection can't
    /// derive a plane.</summary>
    private bool TryReadScreenPlane(PanelSurface surface, out WorldScreenPlane plane)
    {
        plane = default;
        if (!(surface.Block is MyEntity blockEntity)) return false;

        string materialName = TryGetFirstScreenMaterial(blockEntity, surface.SurfaceIdx);
        if (string.IsNullOrEmpty(materialName)) return false;

        if (!_planeResolver.TryResolve(blockEntity, materialName, out var local)) return false;

        // Use the actor's freshest world matrix — the mod's
        // MirrorMeshTilt game-logic component writes the tilted local
        // matrix on the entity, so the plane derived from this matrix
        // ends up at the visibly tilted screen's world position.
        MatrixD blockWorld = _actorMatrix.GetFreshestMatrix(blockEntity);
        plane = WorldScreenPlane.From(in local, in blockWorld);
        return true;
    }

    private static string TryGetFirstScreenMaterial(MyEntity blockEntity, int surfaceIdx)
    {
        var screenRC = blockEntity.Render as Sandbox.Game.Components.MyRenderComponentScreenAreas;
        if (screenRC == null) return null;
        var areas = screenRC.m_screenAreas;
        if (areas == null || areas.Count == 0) return null;
        int idx = (surfaceIdx >= 0 && surfaceIdx < areas.Count) ? surfaceIdx : 0;
        return areas[idx].Material;
    }

    private static long TryGetGridEntityId(PanelSurface surface)
    {
        try
        {
            var grid = surface.Block.CubeGrid;
            return grid?.EntityId ?? 0L;
        }
        catch { return 0L; }
    }
}
