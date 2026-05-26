using System;

namespace ClientPlugin;

/// <summary>
/// Single selector for every slot. Picks by combined score:
///   <c>focus · focusScale + staleness · StalenessWeight</c>
/// where focus is <see cref="FocusScore"/>'s
/// <c>Coverage × LookFactor²⁰ / max(1, DistSq)</c> and staleness is the
/// max ticks-since-last-render across the group's members.
///
/// <para><b>focusScale</b> is the surveillance auto-balancer: per-batch
/// <c>1/√N</c> where N is the count of currently-visible units with
/// non-trivial focus (above <see cref="FocusCompeteThreshold"/>). One
/// focused panel → scale 1 (full focus dominance). Twelve focused
/// panels (e.g. a wall of surveillance monitors) → scale ≈ 0.29 so
/// the centred one doesn't run away with every pick while the
/// peripheral ones starve. Activates automatically when the scene
/// composition demands it; no setting required.</para>
///
/// <para>No focus-threshold gate. Earlier versions used a hard "is the
/// player focused on a panel?" check that put picking on two different
/// code paths — focused-camera scenarios got special handling because
/// a barely-focused camera could otherwise lock the slot. That whole
/// branch is gone: with pure combined score, a mirror's growing
/// staleness naturally outpaces a peripherally-focused camera over time.</para>
///
/// <para>One special case kept: when the player isn't translating AND
/// the highest-scoring unit is a mirror, round-robin ALL visible
/// mirrors equally — stationary mirrors don't ghost so cycling them
/// (rather than locking to the highest-focus one) keeps every rear /
/// side mirror in equal rotation for parked-with-mirrors scenarios.</para>
///
/// <para><b>Hard lock on looked-at mirror while moving</b>: when the
/// player IS translating AND their crosshair is on a mirror (per the
/// orchestrator's ray-vs-plane test, threaded in as
/// <c>lookedAtMirrorIdx</c>), that mirror is returned directly with no
/// scoring. Use-case: many mirrors visible, player using one as a
/// rear-view; without this lock, peer mirrors accumulate staleness and
/// eventually outscore the active one, causing the rear-view to skip
/// frames precisely when smoothness matters most. Other mirrors only
/// refresh once the player stops moving or looks away.</para>
/// </summary>
internal sealed class FocusAndStalenessSelector : IPanelSlotSelector
{
    private const double StalenessWeight = 0.00001;

    // A unit counts as "actively competing" in the surveillance
    // auto-balancer only when it passes BOTH gates:
    //   * focus score > FocusCompeteThreshold (~near-crosshair attention),
    //   * Coverage > CoverageCompeteThreshold  (~big enough on screen).
    // Either alone produces false positives — focus-only catches tiny
    // distant panels that happen to be centred; coverage-only catches
    // big peripheral panels the player isn't looking at. AND'd, the
    // count equals "panels the player is actively paying attention to."
    private const double FocusCompeteThreshold    = 0.0001;
    private const double CoverageCompeteThreshold = 0.009;

    private readonly IRenderUnitScore _focusScore;

    // Cursor used by the not-moving + best-is-mirror fallback to
    // round-robin visible mirrors. Advances on each successful pick;
    // wraps with the unit array.
    private int _mirrorCursor;

    public FocusAndStalenessSelector(IRenderUnitScore focusScore)
    {
        _focusScore = focusScore ?? throw new ArgumentNullException(nameof(focusScore));
    }

    public int PickNext(RenderUnit[] units, int unitCount, bool[] picked, long tickCounter,
                        bool isPlayerMoving, bool isPlayerInCockpit, int lookedAtMirrorIdx)
    {
        if (unitCount <= 0) return -1;

        // Moving-and-aiming-at-a-mirror hard lock: that mirror gets the
        // slot every frame regardless of any other panel's score. The
        // looked-at mirror is the player's active rear-view; it must
        // render every frame to not ghost while moving. Other mirrors
        // accumulate staleness during the lock and only catch up after
        // the player stops moving or stops looking at this mirror.
        if (isPlayerMoving
            && lookedAtMirrorIdx >= 0
            && lookedAtMirrorIdx < unitCount
            && !picked[lookedAtMirrorIdx])
        {
            return lookedAtMirrorIdx;
        }

        // Surveillance auto-balance: scale focus down when many panels
        // compete for attention simultaneously.
        double focusScale = ComputeFocusScale(units, unitCount, picked, tickCounter, isPlayerInCockpit);

        // Combined score for every unpicked unit; track the best.
        int    bestIdx   = -1;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < unitCount; i++)
        {
            if (picked[i]) continue;
            double s = ScoreFor(in units[i], tickCounter, isPlayerInCockpit, focusScale);
            if (s > bestScore) { bestScore = s; bestIdx = i; }
        }
        if (bestIdx < 0) return -1;

        // Not-moving + best-is-mirror: cycle all visible mirrors equally.
        // The "best" mirror is usually the one with highest staleness,
        // and locking to it batch-after-batch would only refresh that
        // one while its neighbours fall further behind. Round-robin
        // gives every visible mirror equal share. Cameras still get
        // picked normally on their own combined score in subsequent
        // batches via PickNext re-calls (slot 0, slot 1+, ...).
        if (!isPlayerMoving
            && units[bestIdx].Group.Members[0].Surface.Mode == PanelMode.Mirror)
        {
            for (int step = 0; step < unitCount; step++)
            {
                int i = (_mirrorCursor + step) % unitCount;
                if (picked[i]) continue;
                if (units[i].Group.Members[0].Surface.Mode != PanelMode.Mirror) continue;
                _mirrorCursor = (i + 1) % unitCount;
                return i;
            }
            // No unpicked mirror — fall through to the combined-score pick.
        }

        return bestIdx;
    }

    private static long MaxMemberStaleness(in RenderUnit u, long tickCounter)
    {
        long best = 0;
        var members = u.Group.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            long s = members[mi].Surface.Staleness(tickCounter);
            if (s > best) best = s;
        }
        return best;
    }

    /// <summary>Combined score = focus·focusScale + staleness×weight.
    /// When the player is piloting a cockpit / seat the focus term is
    /// dropped entirely (vehicle orientation, not player intent,
    /// determines view direction). focusScale is supplied by the caller
    /// (<see cref="ComputeFocusScale"/>) so all units in one batch share
    /// it. Used by both PickNext and the debug-HUD readout
    /// (<see cref="ComputeWithStaleness"/>) so the displayed numbers
    /// match what actually picks.</summary>
    private double ScoreFor(in RenderUnit u, long tickCounter,
                            bool isPlayerInCockpit, double focusScale)
    {
        double f         = isPlayerInCockpit ? 0.0 : _focusScore.Compute(in u, tickCounter) * focusScale;
        long   staleness = MaxMemberStaleness(u, tickCounter);
        return f + staleness * StalenessWeight;
    }

    /// <summary>
    /// Surveillance auto-balance: count how many currently-visible
    /// units pass BOTH the focus-score gate
    /// (<see cref="FocusCompeteThreshold"/>) AND the coverage gate
    /// (<see cref="CoverageCompeteThreshold"/>), and return <c>1/√N</c>
    /// as the scale to apply to every unit's focus term this batch.
    ///
    /// <para>The motivation: <see cref="FocusScore"/> outputs an
    /// absolute value (Coverage × LookFactor²⁰ / DistSq), so when 12
    /// camera panels are visible in a "surveillance center" the centred
    /// one can score ~5× higher than the others and dominate every
    /// pick — peripheral panels then take 10+ seconds between refreshes
    /// because their staleness has to climb past the centred one's
    /// fixed focus lead. Softening focus by 1/√N rebalances: with N=12
    /// the scale is 0.29 so staleness drives most of the ordering and
    /// every panel cycles fairly. With N=1 the scale is 1.0 = exact
    /// previous behaviour, single-target focus uninterrupted.</para>
    ///
    /// <para>Cockpit mode short-circuits to 1.0 because focus is zeroed
    /// in <see cref="ScoreFor"/> anyway — no point counting.</para>
    /// </summary>
    public double ComputeFocusScale(RenderUnit[] units, int unitCount, bool[] picked,
                                    long tickCounter, bool isPlayerInCockpit)
    {
        if (isPlayerInCockpit) return 1.0;
        int competing = 0;
        for (int i = 0; i < unitCount; i++)
        {
            if (picked != null && picked[i]) continue;
            if (units[i].Coverage <= CoverageCompeteThreshold) continue;
            if (_focusScore.Compute(in units[i], tickCounter) <= FocusCompeteThreshold) continue;
            competing++;
        }
        return competing > 1 ? 1.0 / Math.Sqrt(competing) : 1.0;
    }

    // ── Diagnostic surface ───────────────────────────────────────────────

    /// <summary>The combined score that drives picking. Exposed so
    /// <c>PanelDebug</c> can display it on the HUD without duplicating
    /// the formula. Pass the live cockpit flag AND the pre-computed
    /// focus scale (from <see cref="ComputeFocusScale"/>) so the
    /// displayed score matches what PickNext would actually use this
    /// frame.</summary>
    public double ComputeWithStaleness(in RenderUnit u, long tickCounter,
                                       bool isPlayerInCockpit,
                                       double focusScale)
        => ScoreFor(in u, tickCounter, isPlayerInCockpit, focusScale);
}
