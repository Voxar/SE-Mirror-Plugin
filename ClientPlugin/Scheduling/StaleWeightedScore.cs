using System;

namespace ClientPlugin;

/// <summary>
/// Score = <c>staleness × CenterFactor × Coverage / max(1, DistSq)</c>.
/// Used for slot 1+: stale panels gain priority (so far-but-visible
/// panels still refresh), with the usual aim/size/distance bias on
/// top. Staleness is the max over the group's members — each member
/// can be at a different last-rendered tick.
/// </summary>
internal sealed class StaleWeightedScore : IRenderUnitScore
{
    public double Compute(in RenderUnit u, long tickCounter)
    {
        long staleness = 0;
        var members = u.Group.Members;
        for (int mi = 0; mi < members.Count; mi++)
        {
            long s = members[mi].Surface.Staleness(tickCounter);
            if (s > staleness) staleness = s;
        }
        return staleness * u.CenterFactor * u.Coverage / Math.Max(1.0, u.DistSq);
    }
}
