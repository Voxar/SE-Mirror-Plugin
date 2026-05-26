namespace ClientPlugin;

/// <summary>
/// Score = <see cref="RenderUnit.CenterFactor"/>. cos⁴ angle from
/// player-forward to the group's anchor point. "Which panel's
/// anchor is most directly on the player's aim."
/// </summary>
internal sealed class CenterFactorScore : IRenderUnitScore
{
    public double Compute(in RenderUnit u, long tickCounter) => u.CenterFactor;
}
