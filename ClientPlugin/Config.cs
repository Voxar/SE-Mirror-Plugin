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
/// class — same pattern the old <c>MirrorCameraPluginSettings</c>
/// used. Consumers should read on each use rather than cache, since
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
    private bool  _headFix                  = true;
    private float _panelFarClipM            = 20000f;
    private bool  _alwaysGroupTouching      = true;
    private bool  _reportStatus             = true;
    private bool  _renderShadows            = true;
    private bool  _debugHud                 = false;
    private bool  _distanceResolutionScale  = true;
    private float _maxViewDistanceM         = 10f;

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

    [Slider(1f, 3f, 1f, SliderAttribute.SliderType.Integer,
        description: "Maximum panels rendered per frame. Original policy = 1.")]
    public int MaxPerFrame
    {
        get => _maxPerFrame;
        set => SetField(ref _maxPerFrame, value);
    }

    // ── Range ───────────────────────────────────────────────────────

    [Separator("Range")]

    [Slider(1f, 400f, 1f, SliderAttribute.SliderType.Float,
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

    // ── Quality ─────────────────────────────────────────────────────

    [Separator("Quality")]

    [Checkbox(label: "Render shadows",
        description: "Disable if shadows flicker.")]
    public bool RenderShadows
    {
        get => _renderShadows;
        set => SetField(ref _renderShadows, value);
    }

    [Checkbox(label: "Distance resolution LOD",
        description: "Distant panels render at lower resolution.")]
    public bool DistanceResolutionScale
    {
        get => _distanceResolutionScale;
        set => SetField(ref _distanceResolutionScale, value);
    }

    // ── Advanced ────────────────────────────────────────────────────

    [Separator("Advanced")]

    [Checkbox(label: "Head fix",
        description: "Show character head/face during panel renders.")]
    public bool HeadFix
    {
        get => _headFix;
        set => SetField(ref _headFix, value);
    }

    [Checkbox(label: "Always group touching",
        description: "Merge edge-to-edge mirror walls regardless of RT budget.")]
    public bool AlwaysGroupTouching
    {
        get => _alwaysGroupTouching;
        set => SetField(ref _alwaysGroupTouching, value);
    }

    [Checkbox(label: "Report status",
        description: "Push per-panel status to the mod for splash subtitles.")]
    public bool ReportStatus
    {
        get => _reportStatus;
        set => SetField(ref _reportStatus, value);
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
