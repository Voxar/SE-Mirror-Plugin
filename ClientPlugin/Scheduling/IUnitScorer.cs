using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Computes per-group scoring inputs (distance², screen coverage) for
/// the slot scheduler. Writes into a caller-provided
/// <see cref="RenderUnit"/> buffer to avoid allocations.
/// </summary>
internal interface IUnitScorer
{
    /// <summary>
    /// Score each group into <paramref name="dest"/>. Returns the
    /// number of units written (groups with no resolvable distance —
    /// e.g. members missing block refs — are skipped).
    /// </summary>
    /// <param name="viewProjection">Main camera's combined view-
    /// projection matrix, used to project group AABB corners into
    /// NDC for the coverage metric.</param>
    int Score(IReadOnlyList<PanelGroup> groups,
              MatrixD playerWorld,
              MatrixD viewProjection,
              RenderUnit[] dest);
}
