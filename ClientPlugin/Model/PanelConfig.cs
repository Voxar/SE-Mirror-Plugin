using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace ClientPlugin;

/// <summary>
/// Immutable per-surface configuration: rendering mode, the camera
/// block (when <see cref="Mode"/> == <see cref="PanelMode.Camera"/>),
/// and zoom factor. Pushed from the mod's terminal UI through the
/// cross-assembly bridge.
///
/// <para>The camera comes through as an <see cref="IMyCubeBlock"/>
/// reference rather than an entity id — the mod already holds it
/// (from listbox population) and passing the reference removes the
/// per-frame <c>MyEntities.TryGetEntityById</c> hop on the plugin
/// side. The lookup-by-id path had a narrow but real hole where the
/// engine's entity table could transiently miss a present entity
/// (per-thread vs main-thread table swap, brief Closed flag flip);
/// passing the reference closes that hole.</para>
///
/// Reference type (record class) — chosen over a struct so that
/// <see cref="PanelSurface.UpdateConfig"/> can swap configurations
/// atomically. A struct field write of multiple fields is not atomic
/// on x64 and would expose torn reads to the render thread (e.g.
/// new Mode but old CameraBlock). Allocations happen only on
/// config change (rare: terminal UI edits), and config reads are
/// single reference loads — same cost as a struct field read.
///
/// Compiler-generated value equality lets the registry detect "did
/// this surface's config change?" with a single Equals call.
/// </summary>
internal sealed record class PanelConfig(
    PanelMode    Mode,
    IMyCubeBlock CameraBlock,
    float        Zoom,
    float        MirrorAngleDegX,
    float        MirrorAngleDegY,
    float        MirrorAngleDegZ);
