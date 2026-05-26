using VRage.Game.Entity;

namespace ClientPlugin;

/// <summary>
/// Resolves the local-space screen plane of an LCD block instance via
/// mesh introspection. The result depends only on the block's
/// definition + the screen-surface material name, so implementations
/// cache the result process-wide and reuse it across every instance of
/// the same block type with the same screen.
/// </summary>
internal interface IScreenPlaneResolver
{
    /// <summary>
    /// Resolve the local-space plane. Returns false if the block has
    /// no model, the material name doesn't match any submesh, or the
    /// mesh-walk produced a degenerate result (e.g. all triangles
    /// zero-area). Failures are NOT cached — transient block-load
    /// states retry on the next call until the geometry resolves.
    /// </summary>
    bool TryResolve(MyEntity blockEntity, string materialName, out ScreenPlaneInfo info);
}
