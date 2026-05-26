using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin;

/// <summary>
/// Camera math state: view + projection + FOVs + clip planes + the
/// off-axis projection flag. Sufficient to drive
/// <see cref="MyRender11.SetupCameraMatrices"/> via a
/// <see cref="MyRenderMessageSetCameraViewMatrix"/>.
///
/// <para>Immutable readonly struct. Snapshot the main view's matrices
/// with <see cref="CaptureMain"/>; build a panel-specific variant with
/// <see cref="ForMirror"/> / <see cref="ForCamera"/>; write to the
/// engine with <see cref="Apply"/>.</para>
///
/// <para>Performance: this struct is intentionally large
/// (~150 bytes) but never heap-allocated — pass <c>in</c> to avoid
/// copying through method parameter slots.</para>
/// </summary>
internal readonly struct CameraMatrices
{
    public readonly MatrixD ViewMatrix;
    public readonly Matrix  Projection;
    public readonly Matrix  ProjectionFar;
    public readonly Vector3D CameraPosition;

    public readonly float FovH;
    public readonly float FovV;
    public readonly float FOVForSkybox;

    public readonly float NearPlane;
    public readonly float FarPlane;
    public readonly float FarFarPlane;

    public readonly float ProjectionOffsetX;
    public readonly float ProjectionOffsetY;

    public readonly bool IsOffAxisProjection;
    public readonly bool Smooth;
    public readonly int  LastMomentUpdateIndex;

    public CameraMatrices(
        MatrixD viewMatrix, Matrix projection, Matrix projectionFar,
        Vector3D cameraPosition,
        float fovH, float fovV, float fovForSkybox,
        float nearPlane, float farPlane, float farFarPlane,
        float projectionOffsetX, float projectionOffsetY,
        bool isOffAxisProjection, bool smooth, int lastMomentUpdateIndex)
    {
        ViewMatrix          = viewMatrix;
        Projection          = projection;
        ProjectionFar       = projectionFar;
        CameraPosition      = cameraPosition;
        FovH                = fovH;
        FovV                = fovV;
        FOVForSkybox        = fovForSkybox;
        NearPlane           = nearPlane;
        FarPlane            = farPlane;
        FarFarPlane         = farFarPlane;
        ProjectionOffsetX   = projectionOffsetX;
        ProjectionOffsetY   = projectionOffsetY;
        IsOffAxisProjection = isOffAxisProjection;
        Smooth              = smooth;
        LastMomentUpdateIndex = lastMomentUpdateIndex;
    }

    // ── Factories ────────────────────────────────────────────────────

    /// <summary>Snapshot the engine's current camera matrices. Used
    /// at batch start to capture the main view state so per-panel
    /// renders can restore it after each render.</summary>
    public static CameraMatrices CaptureMain()
    {
        var em = MyRender11.Environment.Matrices;
        return new CameraMatrices(
            viewMatrix:           em.ViewD,
            projection:           em.OriginalProjection,
            projectionFar:        em.OriginalProjectionFar,
            cameraPosition:       em.CameraPosition,
            fovH:                 em.FovH,
            fovV:                 em.FovV,
            fovForSkybox:         em.FovH,
            nearPlane:            em.NearClipping,
            farPlane:             em.FarClipping,
            farFarPlane:          em.FarClipping,
            projectionOffsetX:    em.Projection.M31,
            projectionOffsetY:    em.Projection.M32,
            isOffAxisProjection:  false,
            smooth:               false,
            lastMomentUpdateIndex: 0);
    }

    /// <summary>Build camera matrices for a mirror render. FOV is set
    /// to 0 in <see cref="Apply"/> when <see cref="IsOffAxisProjection"/>
    /// is true — the off-axis sentinel that tells
    /// <c>SetupCameraMatricesInternal</c> to use our projection matrix
    /// directly instead of rebuilding one from FOV.</summary>
    public static CameraMatrices ForMirror(in MirrorCamera cam, float farPlaneMeters,
                                           float farFarPlaneMeters)
        => new CameraMatrices(
            viewMatrix:           cam.View,
            projection:           cam.Projection,
            projectionFar:        cam.ProjectionInfiniteFar,
            cameraPosition:       cam.Position,
            fovH:                 cam.FovH,
            fovV:                 cam.FovV,
            fovForSkybox:         cam.FovH,
            nearPlane:            0.1f,
            farPlane:             farPlaneMeters,
            farFarPlane:          farFarPlaneMeters,
            projectionOffsetX:    cam.OffsetX,
            projectionOffsetY:    cam.OffsetY,
            isOffAxisProjection:  true,
            smooth:               false,
            lastMomentUpdateIndex: 0);

    /// <summary>Build camera matrices for a camera-block-mode render.
    /// Uses a standard reverse-Z infinite RH perspective (no off-axis
    /// trickery, no X-flip); FOV is sent through so SE rebuilds the
    /// projection if it needs to.</summary>
    public static CameraMatrices ForCamera(
        MatrixD view, Matrix projection,
        Vector3D cameraPosition,
        float fovH, float fovV,
        float farPlaneMeters,
        float farFarPlaneMeters)
        => new CameraMatrices(
            viewMatrix:           view,
            projection:           projection,
            projectionFar:        projection,
            cameraPosition:       cameraPosition,
            fovH:                 fovH,
            fovV:                 fovV,
            fovForSkybox:         fovH,
            nearPlane:            0.1f,
            farPlane:             farPlaneMeters,
            farFarPlane:          farFarPlaneMeters,
            projectionOffsetX:    0f,
            projectionOffsetY:    0f,
            isOffAxisProjection:  false,
            smooth:               false,
            lastMomentUpdateIndex: 0);

    // ── Apply ────────────────────────────────────────────────────────

    /// <summary>
    /// Write these matrices to the engine. Builds a
    /// <see cref="MyRenderMessageSetCameraViewMatrix"/> from the pool,
    /// hands it to <see cref="MyRender11.SetupCameraMatrices"/>, then
    /// overwrites <c>Environment.Matrices.FovH/FovV</c> directly: for
    /// off-axis projections the engine receives FOV=0 and won't compute
    /// FovH/FovV from the projection, so we set them explicitly.
    /// </summary>
    public void Apply()
    {
        MyRenderMessageSetCameraViewMatrix msg = null;
        try
        {
            msg = MyRenderProxy.MessagePool.Get<MyRenderMessageSetCameraViewMatrix>(
                MyRenderMessageEnum.SetCameraViewMatrix);
            msg.ViewMatrix             = ViewMatrix;
            msg.ProjectionMatrix       = Projection;
            msg.ProjectionFarMatrix    = ProjectionFar;
            // FOV = 0 is the off-axis sentinel (MyRender11.cs:1859-1864).
            // For mirror renders we ship our X-flipped off-axis projection
            // and tell SE to use it as-is; for camera renders FOV > 0 lets
            // SE rebuild a standard perspective from FOV+aspect+near+far.
            msg.FOV                    = IsOffAxisProjection ? 0f : FovH;
            msg.FOVForSkybox           = IsOffAxisProjection ? 0f : FovH;
            msg.NearPlane              = NearPlane;
            msg.FarPlane               = FarPlane;
            // Use the struct's FarFarPlane (orchestrator fills it with
            // the main view's LargeDistanceFarClipping) rather than
            // recomputing FarPlane * 500f locally — keeps panel renders
            // consistent with what the main view ships.
            msg.FarFarPlane            = FarFarPlane;
            msg.CameraPosition         = CameraPosition;
            msg.LastMomentUpdateIndex  = LastMomentUpdateIndex;
            msg.ProjectionOffsetX      = ProjectionOffsetX;
            msg.ProjectionOffsetY      = ProjectionOffsetY;
            msg.Smooth                 = Smooth;
            MyRender11.SetupCameraMatrices(msg);

            // SetupCameraMatricesInternal sets em.FovH = msg.FOV and
            // derives em.FovV from aspect. For off-axis (FOV=0) those
            // become wrong — overwrite directly. Safe because
            // MyEnvironmentMatrices is a class (live ref).
            var em = MyRender11.Environment.Matrices;
            em.FovH = FovH;
            em.FovV = FovV;
        }
        finally
        {
            msg?.Dispose();
        }
    }
}
