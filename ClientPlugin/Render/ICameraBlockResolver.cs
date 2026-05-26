using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace ClientPlugin;

/// <summary>
/// Resolves a camera block reference into a view: its render-side
/// world matrix (jitter-free relative to visible geometry) and its
/// configured FOV with per-LCD zoom applied.
/// </summary>
internal interface ICameraBlockResolver
{
    /// <summary>
    /// Try to resolve the camera block. Returns false when:
    /// <list type="bullet">
    ///   <item><paramref name="cameraBlock"/> is null (no camera
    ///         configured on this LCD).</item>
    ///   <item>The block reference is closed / marked for close
    ///         (engine has begun tearing it down).</item>
    ///   <item>It's not an <c>IMyCameraBlock</c>.</item>
    ///   <item>It's not currently working / functional
    ///         (powered off, broken).</item>
    /// </list>
    /// On failure, <paramref name="failureReason"/> identifies which
    /// of the above bailed — surfaced to the panel splash subtitle so
    /// the user can tell "the camera's powered off" apart from "the
    /// block reference is gone".
    /// </summary>
    bool TryResolve(IMyCubeBlock cameraBlock, float zoom,
                    out CameraBlockView view, out string failureReason);
}
