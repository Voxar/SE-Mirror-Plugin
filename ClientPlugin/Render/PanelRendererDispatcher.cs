using System;

namespace ClientPlugin;

/// <summary>
/// Composite <see cref="IPanelRenderer"/> that delegates to the
/// per-mode strategy. Single point of dispatch — adding a new
/// <see cref="PanelMode"/> means adding a renderer and one switch
/// arm here, not touching the orchestrator or batch loop.
/// </summary>
internal sealed class PanelRendererDispatcher : IPanelRenderer
{
    private readonly IPanelRenderer _mirror;
    private readonly IPanelRenderer _camera;

    public PanelRendererDispatcher(IPanelRenderer mirror, IPanelRenderer camera)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    public bool Render(PanelSurface surface, in PanelRenderContext ctx)
    {
        // Snapshot Mode once: surface.Config can swap under us between
        // reads, and a torn read could send a Camera-mode panel through
        // the mirror renderer (or vice versa). The shortcut property
        // does one Config field read internally.
        switch (surface.Mode)
        {
            case PanelMode.Mirror: return _mirror.Render(surface, in ctx);
            case PanelMode.Camera: return _camera.Render(surface, in ctx);
            default:               return false;
        }
    }
}
