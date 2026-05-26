namespace ClientPlugin;

/// <summary>
/// Runs one complete panel-render batch end-to-end: snapshot main
/// state, refresh group planes, score units, cull, pick slots,
/// dispatch renderers, restore main state. Called from the
/// <c>MyRender11.DrawGameScene</c> Harmony prefix once per frame.
/// </summary>
internal interface IPanelBatchOrchestrator
{
    /// <summary>Run one batch. Errors are caught and logged; never
    /// propagates to the engine.</summary>
    void RunBatch();
}
