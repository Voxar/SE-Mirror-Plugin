namespace ClientPlugin;

/// <summary>
/// Rendering mode chosen for a registered LCD surface. Wire-compatible
/// with the mod's PanelMode int (0 = Mirror, 1 = Camera). New modes get
/// new IPanelRenderer implementations; the enum value is the dispatch
/// key.
/// </summary>
internal enum PanelMode
{
    Mirror = 0,
    Camera = 1,
}
