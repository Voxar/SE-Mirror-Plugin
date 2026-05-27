using Sandbox.Definitions;

namespace ClientPlugin;

/// <summary>
/// Drops groups whose lead block is farther from the viewer than the
/// block definition's <c>MaxScreenRenderDistance</c> — SE's own
/// per-block setting for how far away the LCD's screen content stops
/// being rendered. No point in our plugin rendering panels SE itself
/// has given up on.
///
/// <para>Typical values from vanilla content:</para>
/// <list type="bullet">
///   <item>25m — small-grid Corner LCDs (the 0.5m square ones)</item>
///   <item>60m — large-grid Corner LCDs, SmallTextPanel</item>
///   <item>120m — Large/SmallLCDPanel + Wide variants, LargeTextPanel</item>
///   <item>400m — Sparks of the Future jumbotron-style LCDs</item>
/// </list>
///
/// <para>Reads from <see cref="MyTextPanelDefinition.MaxScreenRenderDistance"/>.
/// Blocks whose definition isn't a text panel (cameras backing a camera-
/// mode panel still typically have a text-panel def since the LCD
/// surface is the same hardware) or whose value is 0/missing are kept
/// — this predicate only culls when SE has an explicit value.</para>
/// </summary>
internal sealed class MaxScreenRenderDistanceCull : IPanelCull
{
    public bool ShouldKeep(PanelGroup group, in CullContext ctx)
    {
        var lead = group.Members[0].Surface;
        var leadBlock = lead.Block;
        if (leadBlock == null) return false;

        if (!MyDefinitionManager.Static.TryGetDefinition<MyTextPanelDefinition>(
                leadBlock.BlockDefinition, out var textPanelDef)
            || textPanelDef == null)
            return true;

        float maxDist = textPanelDef.MaxScreenRenderDistance;
        if (maxDist <= 0f) return true;

        // Use closest-member distance from CullContext (computed once
        // per unit by UnitScorer). Lead-only would falsely drop wide
        // groups whose lead sits at the far end of the wall.
        return ctx.GroupClosestDistSq <= (double)maxDist * maxDist;
    }
}
