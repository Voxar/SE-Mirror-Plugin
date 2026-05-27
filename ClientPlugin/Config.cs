using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClientPlugin;

/// <summary>
/// Runtime plugin settings. Properties are decorated with the
/// template's settings-element attributes so <c>SettingsGenerator</c>
/// builds the config dialog automatically; INotifyPropertyChanged is
/// the template's wire for live-update.
///
/// <para>Implements <see cref="IMirrorPluginSettings"/> so plugin
/// services that take the interface stay decoupled from this concrete
/// class. Consumers should read on each use rather than cache, since
/// the dialog mutates fields live.</para>
///
/// <para>Persistence: <see cref="ConfigStorage"/> XML-serializes this
/// class. Saved on dialog close via <c>SettingsScreen.OnRemoved</c>.</para>
/// </summary>
public class Config : INotifyPropertyChanged, IMirrorPluginSettings
{
    // ── Backing fields ──────────────────────────────────────────────

    private bool  _enabled                  = true;
    private int   _maxPerFrame              = 1;
    private float _panelFarClipM            = 20000f;
    private bool  _disableShadows           = false;
    private bool  _debugHud                 = false;
    private float _lodDistanceFactor        = 2f;
    private float _maxViewDistanceM         = 40f;
    private bool  _renderOnPauseScreen      = false;

    // ── Dialog title ────────────────────────────────────────────────

    public readonly string Title = "Mirror Camera Panels";

    // ── Master ──────────────────────────────────────────────────────

    [Separator("Master")]

    [Checkbox(description: "Master enable for all panel renders.")]
    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    // ── Performance ─────────────────────────────────────────────────

    [Separator("Performance")]

    [Slider(1f, 3f, 1f, SliderAttribute.SliderType.Integer,
        description: "Maximum panels rendered per frame. Original policy = 1.")]
    public int MaxPerFrame
    {
        get => _maxPerFrame;
        set => SetField(ref _maxPerFrame, value);
    }

    [Slider(5f, 400f, 1f, SliderAttribute.SliderType.Float,
        label: "Max view distance (m)",
        description: "Panels farther than this from the viewer don't render.")]
    public float MaxViewDistanceM
    {
        get => _maxViewDistanceM;
        set => SetField(ref _maxViewDistanceM, value);
    }

    [Slider(1300f, 100000f, 100f, SliderAttribute.SliderType.Float,
        label: "Far clip (m)",
        description: "Far plane distance for panel renders.")]
    public float PanelFarClipM
    {
        get => _panelFarClipM;
        set => SetField(ref _panelFarClipM, value);
    }

    [Slider(0.1f, 5.1f, 0.1f, SliderAttribute.SliderType.Float,
        label: "Resolution scale",
        description: "Higher = distant panels stay high-res longer. Max = always screen resolution.",
        maxLabel: "Max")]
    public float LodDistanceFactor
    {
        get => _lodDistanceFactor;
        set => SetField(ref _lodDistanceFactor, value);
    }

    [Checkbox(label: "Render on pause screen",
        description: "Keep panels rendering when the game is paused. Off by default to free GPU.")]
    public bool RenderOnPauseScreen
    {
        get => _renderOnPauseScreen;
        set => SetField(ref _renderOnPauseScreen, value);
    }

    // ── Troubleshooting ─────────────────────────────────────────────

    [Separator("Troubleshooting")]

    [Checkbox(label: "Disable shadows",
        description: "Suppress the directional-shadows pass in panel renders. Try this if shadows flicker in reflections.")]
    public bool DisableShadows
    {
        get => _disableShadows;
        set => SetField(ref _disableShadows, value);
    }

    // ── Debug ───────────────────────────────────────────────────────

    [Separator("Debug")]

    [Checkbox(label: "Debug HUD",
        description: "Overlay scored panels and signals on screen.")]
    public bool DebugHud
    {
        get => _debugHud;
        set => SetField(ref _debugHud, value);
    }

    // ── INotifyPropertyChanged boilerplate ──────────────────────────

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
