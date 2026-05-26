using System;

namespace ClientPlugin;

/// <summary>
/// Generic slot selector: picks the not-yet-picked unit with the
/// maximum score produced by the injected <see cref="IRenderUnitScore"/>.
/// Ties (identical score) are broken by closer distance² winning.
///
/// <para>The selector knows nothing about WHICH formula it's using —
/// the formula is the score function. This way the composition root
/// makes "slot N picks by formula X" explicit, and the formula stays
/// in its own class.</para>
/// </summary>
internal sealed class ArgmaxSelector : IPanelSlotSelector
{
    private readonly IRenderUnitScore _score;

    public ArgmaxSelector(IRenderUnitScore score)
    {
        _score = score ?? throw new ArgumentNullException(nameof(score));
    }

    public int PickNext(RenderUnit[] units, int unitCount, bool[] picked, long tickCounter,
                        bool isPlayerMoving, bool isPlayerInCockpit, int lookedAtMirrorIdx)
    {
        int bestIdx = -1;
        double bestScore   = double.MinValue;
        double bestTieDist = double.MaxValue;
        for (int i = 0; i < unitCount; i++)
        {
            if (picked[i]) continue;
            double s = _score.Compute(in units[i], tickCounter);
            if (s > bestScore
                || (s == bestScore && units[i].DistSq < bestTieDist))
            {
                bestScore   = s;
                bestTieDist = units[i].DistSq;
                bestIdx     = i;
            }
        }
        return bestIdx;
    }
}
