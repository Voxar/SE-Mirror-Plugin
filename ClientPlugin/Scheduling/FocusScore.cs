using System;

namespace ClientPlugin;

/// <summary>
/// Score = <c>Coverage × LookFactor^N / max(1, DistSq)</c>, with the
/// center-bias exponent hard-coded to a single value tuned through
/// in-game iteration (no longer user-configurable — the setting
/// rarely got moved off its sweet spot and only added GUI clutter).
///
/// <list type="bullet">
///   <item><b>Coverage</b> — fraction of screen the panel actually
///         covers (proper polygon area).</item>
///   <item><b>LookFactor^N</b> — LookFactor is cos⁴ of the angle to
///         the closest point on the closest member's rect. Raising
///         it to the Nth biases hard toward the panel under the
///         crosshair. N=20 → cos⁸⁰, which is very aggressive
///         falloff — a panel even a few degrees off-center scores
///         orders of magnitude lower than one directly aimed at.</item>
///   <item><b>1 / max(1, DistSq)</b> — explicit closeness penalty.
///         Coverage alone doesn't capture distance when panels are
///         different sizes — a large far panel can project to the
///         same screen area as a small close one. The DistSq factor
///         tiebreaks toward whichever is closer to the viewer.
///         The <c>max(1, ...)</c> floor avoids the penalty becoming
///         a boost at sub-1m distances.</item>
/// </list>
/// </summary>
internal sealed class FocusScore : IRenderUnitScore
{
    private const double CenterBiasExponent = 20.0;

    public double Compute(in RenderUnit u, long tickCounter)
    {
        double bias = Math.Pow(u.LookFactor, CenterBiasExponent);
        return u.Coverage * bias / Math.Max(1.0, u.DistSq);
    }
}
