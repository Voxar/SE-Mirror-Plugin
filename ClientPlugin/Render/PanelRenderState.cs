using VRageMath;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Composite engine state captured around a panel render: camera math,
/// render settings, and the last camera position used by SE for
/// temporal effects (motion vectors, TAA). Apply pushes everything to
/// <see cref="MyRender11"/> globals; capture reads everything back.
/// Together with <see cref="MainCameraStateGuard"/> this forms an RAII
/// scope that swaps in panel state for the duration of one render and
/// restores the captured main state on Dispose.
///
/// <para>Resolution (ResolutionI / ViewportResolution) is NOT part of
/// this state and is deliberately not mutated by Apply. Overriding
/// <c>m_resolution</c> per panel render makes
/// <see cref="MyBorrowedRwTextureManager"/> mint new borrow keys for
/// each unique size; combined with the 16-frame retention window and
/// the hard-capped 16-slot global <see cref="MyDepthStencilManager"/>
/// pool, this can exhaust the pool and crash the engine. Panel
/// renders run at the main view's resolution; the final blit scales
/// to the LCD offscreen.</para>
/// </summary>
internal readonly struct PanelRenderState
{
    public readonly CameraMatrices Camera;
    public readonly RenderSettings Settings;
    public readonly Vector3D       LastCameraPosition;

    public PanelRenderState(in CameraMatrices camera, in RenderSettings settings,
                            Vector3D lastCameraPosition)
    {
        Camera             = camera;
        Settings           = settings;
        LastCameraPosition = lastCameraPosition;
    }

    /// <summary>Snapshot the engine's current state. Sim/render-thread
    /// agnostic for the captures themselves; reading from
    /// MyRender11.Environment.Matrices is single-threaded on the render
    /// thread which is where this is called.</summary>
    public static PanelRenderState CaptureMain() => new(
        camera:             CameraMatrices.CaptureMain(),
        settings:           RenderSettings.CaptureMain(),
        lastCameraPosition: MyCommon.m_lastCameraPosition);

    /// <summary>Build the panel-render state for a mirror surface.</summary>
    public static PanelRenderState ForMirror(in PanelRenderState mainSnapshot,
                                             in MirrorCamera cam,
                                             float farPlaneMeters,
                                             float farFarPlaneMeters,
                                             bool enableShadows) => new(
        camera:             CameraMatrices.ForMirror(cam, farPlaneMeters, farFarPlaneMeters),
        settings:           RenderSettings.ForMirror(mainSnapshot.Settings, enableShadows),
        // Keep main view's last camera position so SE's TAA / motion
        // vectors aren't polluted by our reflected eye.
        lastCameraPosition: mainSnapshot.LastCameraPosition);

    /// <summary>Build the panel-render state for a camera-mode surface.</summary>
    public static PanelRenderState ForCamera(in PanelRenderState mainSnapshot,
                                             MatrixD view, Matrix projection,
                                             Vector3D cameraPosition,
                                             float fovH, float fovV,
                                             float farPlaneMeters,
                                             float farFarPlaneMeters,
                                             bool enableShadows) => new(
        camera:             CameraMatrices.ForCamera(view, projection,
                                                     cameraPosition, fovH, fovV,
                                                     farPlaneMeters, farFarPlaneMeters),
        settings:           RenderSettings.ForCamera(mainSnapshot.Settings, enableShadows),
        lastCameraPosition: mainSnapshot.LastCameraPosition);

    /// <summary>Push every field to <see cref="MyRender11"/>. Order
    /// matters: settings before the camera (the lodding write triggers
    /// a manager notification that's cheap when the value is already
    /// at the new target).</summary>
    public void Apply()
    {
        Settings.Apply();
        Camera.Apply();
        MyCommon.m_lastCameraPosition = LastCameraPosition;
    }
}
