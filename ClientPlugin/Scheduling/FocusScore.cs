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
    public double Compute(in RenderUnit u, long tickCounter)
    {
        // LookFactor^20 via repeated squaring: x^2, x^4, x^8, x^16, x^20.
        // Math.Pow's variable-exponent path takes ~25-30 ns each; this is
        // ~5 ns. Compute is called twice per unit per slot (focus-scale
        // normaliser + argmax loop) so per-batch savings scale with N.
        double x  = u.LookFactor;
        double x2 = x  * x;
        double x4 = x2 * x2;
        double x8 = x4 * x4;
        double x16 = x8 * x8;
        double bias = x16 * x4;
        return u.Coverage * bias / Math.Max(1.0, u.DistSq);
    }
}
