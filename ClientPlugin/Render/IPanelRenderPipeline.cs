namespace ClientPlugin;

/// <summary>
/// The lightweight per-panel rendering pipeline:
/// cull → gbuffer → lighting → tonemap → billboards → fxaa → finalize.
/// One call drives a complete frame's worth of work for a single panel
/// (or coplanar group) and ends with the caller's
/// <see cref="IRenderViewFinalizer"/> consuming the post-processed
/// result.
///
/// <para>Implementations set <see cref="PanelRenderScope"/> for the
/// duration so Harmony patches that gate on "are we drawing a panel?"
/// can bypass engine work that would otherwise corrupt main view.</para>
/// </summary>
internal interface IPanelRenderPipeline
{
    /// <summary>Run the pipeline and pass the post-process result to
    /// <paramref name="finalizer"/>. Returns true on success, false if
    /// any stage threw (the exception is logged but not re-raised, so
    /// one bad panel doesn't tear down the batch).</summary>
    bool RenderInto<TFinalizer>(in TFinalizer finalizer)
        where TFinalizer : struct, IRenderViewFinalizer;
}
