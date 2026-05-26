using System;

namespace ClientPlugin;

/// <summary>
/// Composes <see cref="IPanelCull"/> implementations into a chain.
/// Each cull is applied in order; the first rejection short-circuits
/// the remaining checks. Order matters for performance (cheap
/// rejections first: range → facing → look → frustum).
/// </summary>
internal sealed class PanelCullChain
{
    private readonly IPanelCull[] _culls;

    public PanelCullChain(params IPanelCull[] culls)
    {
        _culls = culls ?? throw new ArgumentNullException(nameof(culls));
    }

    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        for (int i = 0; i < _culls.Length; i++)
            if (!_culls[i].ShouldKeep(group, in ctx)) return false;
        return true;
    }

    /// <summary>Convenience: build the default chain used by the
    /// orchestrator. Order is cheap-rejects-first:
    /// max-screen-render → range → facing → look → frustum.
    ///
    /// <para>Panel-vs-panel occlusion runs separately in
    /// <c>PanelBatchOrchestrator.OcclusionCullPanelToPanel</c> after
    /// this chain, since it needs the projected NDC quad which is
    /// computed by <see cref="UnitScorer"/>.</para></summary>
    public static PanelCullChain Default(IMirrorPluginSettings settings) => new(
        new MaxScreenRenderDistanceCull(),
        new RangeCull(settings),
        new FacingCull(),
        new LookDirectionCull(),
        new FrustumCull());
}
