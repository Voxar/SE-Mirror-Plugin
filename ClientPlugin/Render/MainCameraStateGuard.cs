namespace ClientPlugin;

/// <summary>
/// RAII scope that applies a panel-render state and guarantees the
/// captured main-view state is restored on scope exit — even when the
/// guarded code throws.
///
/// <para>Declared as <c>ref struct</c> so it cannot be boxed,
/// captured by a lambda, or stored in a field; the C# compiler emits
/// a hidden try/finally around <c>using</c> blocks that calls
/// <see cref="Dispose"/>. Zero heap allocation per render.</para>
///
/// <para>Usage:</para>
/// <code>
/// var main = PanelRenderState.CaptureMain();
/// var panel = PanelRenderState.ForMirror(main, cam, settings.PanelFarClipM);
/// using (var _ = MainCameraStateGuard.Push(in main, in panel))
/// {
///     pipeline.RenderInto(blitFinalizer);
/// }
/// // main is now re-applied to the engine.
/// </code>
/// </summary>
internal ref struct MainCameraStateGuard
{
    private PanelRenderState _restore;
    private bool _active;

    /// <summary>
    /// Apply <paramref name="applied"/> to the engine immediately and
    /// return a guard that re-applies <paramref name="restore"/> on
    /// dispose. Pass <c>in</c> on both to avoid copying the (~150-byte)
    /// state structs into stack slots twice.
    /// </summary>
    public static MainCameraStateGuard Push(
        in PanelRenderState restore, in PanelRenderState applied)
    {
        applied.Apply();
        return new MainCameraStateGuard(restore);
    }

    private MainCameraStateGuard(in PanelRenderState restore)
    {
        _restore = restore;
        _active  = true;
    }

    public void Dispose()
    {
        if (!_active) return;
        _active = false;
        _restore.Apply();
    }
}
