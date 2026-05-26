namespace ClientPlugin;

/// <summary>
/// Score = <see cref="RenderUnit.LookFactor"/>. Picks "the mirror
/// the player is looking at". Ties (both panels aim-hit, LookFactor
/// = 1.0) fall through to the selector's distance tiebreak — so a
/// small foreground mirror occluding a larger background one wins
/// because it's closer.
/// </summary>
internal sealed class LookFactorScore : IRenderUnitScore
{
    public double Compute(in RenderUnit u, long tickCounter) => u.LookFactor;
}
