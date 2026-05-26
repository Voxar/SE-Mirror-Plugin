namespace ClientPlugin;

/// <summary>
/// Process-wide "we are inside a panel render right now" flag. Read
/// by Harmony patches that need to bypass engine work that would
/// corrupt the main view if it ran during a panel render
/// (e.g. <c>MyEnvironmentProbe.UpdateCullQuery</c> would otherwise
/// capture the panel-eye frustum into the env-probe cubemap), or
/// that need to fix engine state for the panel-render path
/// (e.g. <c>MyFoliageRenderingPass</c> needs depth-clip enabled).
///
/// <para>Was previously <c>[ThreadStatic]</c>, but
/// <c>MyRenderScheduler</c> dispatches some passes (notably
/// foliage) as jobs on the worker thread pool — the panel-render
/// flag set on the main render thread would not be visible to
/// patches that fire on a worker. Panel renders run in the
/// <c>DrawGameScene</c> prefix and complete before the engine's
/// main view starts, so there is no overlap that requires
/// thread-locality; a plain static is correct.</para>
///
/// <para>Use <see cref="Enter"/> to get a <c>ref struct</c> scope
/// guaranteed to clear the flag on dispose. Cannot be boxed or
/// escape its stack frame.</para>
/// </summary>
internal static class PanelRenderScope
{
    private static volatile bool _isDrawing;

    /// <summary>True while a panel render is in progress anywhere in
    /// the process.</summary>
    public static bool IsDrawing => _isDrawing;

    /// <summary>Set the flag and return a guard that clears it on
    /// dispose. Use inside a <c>using</c> block to guarantee restore
    /// even on exception.</summary>
    public static Scope Enter()
    {
        _isDrawing = true;
        return default;
    }

    internal ref struct Scope
    {
        public void Dispose()
        {
            _isDrawing = false;
        }
    }
}
