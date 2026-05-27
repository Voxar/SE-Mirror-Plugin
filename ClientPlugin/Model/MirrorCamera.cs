using System;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// A reflected virtual camera that renders a mirror's view. Owns every
/// piece of mirror math: eye reflection across the plane, screen-aligned
/// left-handed basis, off-axis perspective whose near-plane bounds match
/// the LCD rectangle, and the projection X-flip that keeps the world-
/// to-NDC determinant positive (so standard back-face culling works
/// without a rasterizer hook).
///
/// Identical math for single panels and coplanar groups — only the
/// rectangle differs. Construct via <see cref="TryBuild"/> /
/// <see cref="TryBuildForPanel"/> / <see cref="TryBuildForGroup"/>.
/// On "viewer behind plane" the factories return <c>false</c> and emit
/// no camera.
///
/// Immutable <c>readonly struct</c>: no per-frame heap allocation.
/// </summary>
internal readonly struct MirrorCamera
{
    /// <summary>Reflected camera world matrix (LH basis, det = -1).</summary>
    public readonly MatrixD CamWorld;

    /// <summary>Pre-inverted view matrix. Consumers don't re-invert.</summary>
    public readonly MatrixD View;

    /// <summary>X-flipped reverse-Z FINITE-far RH off-axis projection.
    /// What scene geometry is clipped against.</summary>
    public readonly Matrix  Projection;

    /// <summary>Same as <see cref="Projection"/> but reverse-Z INFINITE
    /// far. Passed as <c>projectionFar</c> / <c>ProjectionForSkybox</c>
    /// so the skybox + distant atmospheric / planet-impostor renders
    /// see geometry beyond the finite far plane.</summary>
    public readonly Matrix  ProjectionInfiniteFar;

    /// <summary>Viewer's positive distance to the plane.</summary>
    public readonly double  SignedDist;

    public readonly float   FovH;
    public readonly float   FovV;
    public readonly float   HalfNearWidth;
    public readonly float   HalfNearHeight;

    public Vector3D Position => CamWorld.Translation;
    public float    OffsetX  => Projection.M31;
    public float    OffsetY  => Projection.M32;

    /// <summary>Below this signed distance the camera is considered
    /// behind the plane (or close enough that the near plane degenerates)
    /// and the factories return false. 1mm — small enough that a
    /// viewer pressed right against the panel still renders, large
    /// enough that the off-axis projection math doesn't collapse at
    /// exactly-zero distance.</summary>
    public const double MinSignedDist = 0.001;

    private MirrorCamera(
        MatrixD camWorld, MatrixD view, Matrix projection, Matrix projectionInfiniteFar,
        double signedDist, float fovH, float fovV,
        float halfNearWidth, float halfNearHeight)
    {
        CamWorld              = camWorld;
        View                  = view;
        Projection            = projection;
        ProjectionInfiniteFar = projectionInfiniteFar;
        SignedDist            = signedDist;
        FovH                  = fovH;
        FovV                  = fovV;
        HalfNearWidth         = halfNearWidth;
        HalfNearHeight        = halfNearHeight;
    }

    // ── Factories ────────────────────────────────────────────────────

    /// <summary>
    /// Build a mirror camera for an arbitrary rect in a plane. Returns
    /// false when the viewer is on (or behind) the plane.
    /// </summary>
    public static bool TryBuild(in MirrorRectInPlane rect, Vector3D eye,
                                float farPlane,
                                out MirrorCamera camera)
    {
        double signedDist = rect.SignedDistanceFrom(eye);
        if (signedDist < MinSignedDist) { camera = default; return false; }

        // Reflect the eye across the plane.
        Vector3D camPos = eye - 2.0 * signedDist * rect.Normal;

        // Screen-aligned LH basis. camForward = +Normal so the camera
        // looks BACK through the screen toward the viewer; camBack =
        // -Normal occupies row 3.
        Vector3D camBack = -rect.Normal;
        MatrixD camWorld = default;
        camWorld.M11 = rect.BasisU.X; camWorld.M12 = rect.BasisU.Y; camWorld.M13 = rect.BasisU.Z; camWorld.M14 = 0;
        camWorld.M21 = rect.BasisV.X; camWorld.M22 = rect.BasisV.Y; camWorld.M23 = rect.BasisV.Z; camWorld.M24 = 0;
        camWorld.M31 = camBack.X;     camWorld.M32 = camBack.Y;     camWorld.M33 = camBack.Z;     camWorld.M34 = 0;
        camWorld.M41 = camPos.X;      camWorld.M42 = camPos.Y;      camWorld.M43 = camPos.Z;      camWorld.M44 = 1;

        // Off-axis frustum bounds at the near plane (= the LCD plane).
        // Express the rect center relative to camPos in (BasisU, BasisV).
        Vector3D toCenter = rect.Center - camPos;
        double offsetX = Vector3D.Dot(toCenter, rect.BasisU);
        double offsetY = Vector3D.Dot(toCenter, rect.BasisV);
        double halfW = 0.5 * rect.Width;
        double halfH = 0.5 * rect.Height;

        float n = (float)signedDist;
        float l = (float)(offsetX - halfW);
        float r = (float)(offsetX + halfW);
        float b = (float)(offsetY - halfH);
        float t = (float)(offsetY + halfH);

        // Reverse-Z FINITE-far RH off-axis projection. Same M11/M22
        // (x/y scale) and M31/M32 (off-axis offset) as the off-axis
        // infinite case; M33/M43 derive from SE's own
        // Matrix.CreatePerspectiveFovRhComplementary formula:
        //   M33 = -f/(n-f) - 1   = n/(f-n)   (positive, ~0 for f >> n)
        //   M43 = -n*f/(n-f)     = n*f/(f-n) (positive, ≈ n)
        //   M34 = -1
        // Vs infinite-far (M33=0, M43=n) this clips geometry at the
        // farPlane distance so SE's CPU-side scene cull skips
        // beyond-far objects.
        float f = Math.Max(farPlane, n + 0.001f);
        Matrix proj = default;
        proj.M11 = 2f * n / (r - l);
        proj.M22 = 2f * n / (t - b);
        proj.M31 = (l + r) / (r - l);
        proj.M32 = (t + b) / (t - b);
        proj.M33 = -f / (n - f) - 1f;
        proj.M34 = -1f;
        proj.M43 = -n * f / (n - f);
        proj.M44 = 0f;

        // Reverse-Z INFINITE-far off-axis projection (M33=0, M43=n).
        // Used as ProjectionForSkybox so atmospheres / distant
        // impostors render beyond the finite far plane.
        Matrix projInf = default;
        projInf.M11 = proj.M11;
        projInf.M22 = proj.M22;
        projInf.M31 = proj.M31;
        projInf.M32 = proj.M32;
        projInf.M33 = 0f;
        projInf.M34 = -1f;
        projInf.M43 = n;
        projInf.M44 = 0f;

        // X-flip: post-multiply by Scale(-1, 1, 1). Negates column 1.
        // Combined with the LH view this makes the world→NDC determinant
        // positive, so standard CullBack rasterizer state works. Apply
        // to BOTH projections.
        proj.M11    = -proj.M11;
        proj.M31    = -proj.M31;
        projInf.M11 = -projInf.M11;
        projInf.M31 = -projInf.M31;

        float halfNearW = (float)halfW;
        float halfNearH = (float)halfH;
        float invN = 1f / Math.Max(n, 0.001f);
        float fovH = 2f * (float)Math.Atan(halfNearW * invN);
        float fovV = 2f * (float)Math.Atan(halfNearH * invN);

        camera = new MirrorCamera(
            camWorld, MatrixD.Invert(camWorld), proj, projInf,
            signedDist, fovH, fovV, halfNearW, halfNearH);
        return true;
    }

    /// <summary>
    /// Single-panel convenience. Front-side only — the viewer must be
    /// on the side the screen normal points to. (An earlier "flip the
    /// plane when behind a double-sided panel" path was explicitly
    /// removed by the user; do not reintroduce it.)
    /// </summary>
    public static bool TryBuildForPanel(
        in WorldScreenPlane plane, Vector3D eye, float farPlane,
        out MirrorCamera camera)
        => TryBuild(MirrorRectInPlane.FromCenteredPlane(plane), eye, farPlane, out camera);

    /// <summary>
    /// Coplanar-group convenience. No double-sided fallback — groups
    /// don't form across transparent boundaries and don't get a third-
    /// person fallback view.
    /// </summary>
    public static bool TryBuildForGroup(PanelGroup group, Vector3D eye,
                                        float farPlane,
                                        out MirrorCamera camera)
        => TryBuild(MirrorRectInPlane.FromGroup(group), eye, farPlane, out camera);
}
