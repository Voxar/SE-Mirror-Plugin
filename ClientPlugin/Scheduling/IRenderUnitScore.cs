namespace ClientPlugin;

/// <summary>
/// Combines a <see cref="RenderUnit"/>'s per-batch scoring inputs
/// (CenterFactor, Coverage, LookFactor, DistSq) into a single
/// double-valued rank. Implementations are pure functions of the
/// unit and the current tick — no internal state, safe to reuse
/// across batches.
///
/// <para>The selector (<see cref="ArgmaxSelector"/>) is generic and
/// just picks the unit with the maximum score. The scoring formula
/// lives here, in its own class, so the composition root makes
/// "what does slot N pick by" explicit.</para>
/// </summary>
internal interface IRenderUnitScore
{
    /// <summary>
    /// Score for the given unit. Larger = preferred. Selectors
    /// ignore the magnitude beyond ordering — only the relative
    /// ranking matters.
    /// </summary>
    double Compute(in RenderUnit u, long tickCounter);
}
