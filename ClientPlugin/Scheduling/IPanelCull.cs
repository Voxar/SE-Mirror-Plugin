namespace ClientPlugin;

/// <summary>
/// One step in the visibility cull chain. Implementations decide
/// whether a candidate <see cref="PanelGroup"/> should be kept for
/// scheduling consideration this batch. Returning false drops the
/// group from the pick pool.
///
/// <para>Composed by <see cref="PanelCullChain"/> in a fixed order;
/// each cull is independently testable.</para>
/// </summary>
internal interface IPanelCull
{
    /// <summary>Return true to keep, false to drop.</summary>
    bool ShouldKeep(PanelGroup group, in CullContext ctx);
}
