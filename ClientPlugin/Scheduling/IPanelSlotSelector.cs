namespace ClientPlugin;

/// <summary>
/// Strategy for picking the next unit from the remaining candidates in
/// a batch's slot loop. Different slots use different strategies:
/// <list type="bullet">
///   <item>Slot 0 → <see cref="CenterFactorSelector"/> (purely "what the
///         player is looking at" — no staleness term)</item>
///   <item>Slot 1+ → <see cref="StaleWeightedSelector"/> (staleness ×
///         centerFactor ÷ distSq, so far-but-stale panels still refresh)</item>
/// </list>
/// </summary>
internal interface IPanelSlotSelector
{
    /// <summary>
    /// Return the index in <paramref name="units"/> of the best
    /// remaining candidate, or -1 if every unit was already picked.
    /// <paramref name="unitCount"/> is the valid prefix length of the
    /// units array (callers reuse arrays larger than needed).
    /// <paramref name="picked"/>[i] == true means unit i was already
    /// chosen in a previous slot of this batch.
    /// <paramref name="isPlayerMoving"/> is true when the player's
    /// world translation has changed since the previous batch beyond
    /// a small jitter threshold — selectors that care about mirror
    /// ghosting (which only manifests on translation, not rotation)
    /// can use this to relax their realtime guarantees when the player
    /// is stationary.
    /// <paramref name="isPlayerInCockpit"/> is true when the player is
    /// piloting a ship controller (cockpit, seat, control station,
    /// cryo). View direction in a vehicle is dictated by the vehicle,
    /// not by the player aiming AT a specific panel — selectors that
    /// weight by focus / look-at score should drop that term in this
    /// case so a peripheral cockpit camera doesn't starve rear-view
    /// mirrors.
    /// <paramref name="lookedAtMirrorIdx"/> is the index of the mirror
    /// panel under the crosshair (ray-vs-plane authoritative, computed
    /// by the orchestrator once per batch), or -1 if no mirror is being
    /// aimed at. Selectors that want a hard "looking at this mirror =
    /// render it" lock use this rather than re-deriving it from focus
    /// score.
    /// </summary>
    int PickNext(RenderUnit[] units, int unitCount, bool[] picked, long tickCounter,
                 bool isPlayerMoving, bool isPlayerInCockpit, int lookedAtMirrorIdx);
}
