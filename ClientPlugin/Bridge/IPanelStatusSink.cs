namespace ClientPlugin;

/// <summary>
/// Plugin → mod status channel. Implementations write per-panel
/// state strings that the mod-side TSS scripts read and show as the
/// LCD splash subtitle. One call per state change; the mod doesn't
/// poll for updates beyond its own Update10 redraw cadence, so
/// over-eager writes are wasted but harmless.
///
/// <para>Status vocabulary is free-form strings — the mod just
/// displays whatever is written. Typical values:</para>
/// <list type="bullet">
///   <item><c>"found"</c> — plugin saw the panel registered.</item>
///   <item><c>"rendered"</c> — last batch picked and rendered the panel.</item>
///   <item><c>"failed: &lt;reason&gt;"</c> — last attempt failed; reason
///         from <see cref="PanelSurface.LastFailure"/>.</item>
/// </list>
///
/// <para>Calls must be cheap — the orchestrator emits per panel per
/// batch. Implementations are responsible for skipping no-op writes
/// (status unchanged from last call) if that matters.</para>
/// </summary>
internal interface IPanelStatusSink
{
    void Report(PanelSurface surface, string status);
}
