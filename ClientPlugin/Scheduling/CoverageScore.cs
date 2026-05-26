namespace ClientPlugin;

/// <summary>
/// Score = <see cref="RenderUnit.Coverage"/>. Fraction of the
/// screen the panel actually covers (proper polygon-area).
/// "Which panel fills the most of the view."
/// </summary>
internal sealed class CoverageScore : IRenderUnitScore
{
    public double Compute(in RenderUnit u, long tickCounter) => u.Coverage;
}
