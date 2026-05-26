using System;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Shared geometric crosshair-vs-panel test. Given the player's view
/// matrix and the in-view RenderUnit set, returns the index of the
/// closest panel whose union AABB the forward ray actually pierces —
/// or -1 if no panel is under the crosshair.
///
/// <para>Used by:
/// <list type="bullet">
///   <item><see cref="PanelBatchOrchestrator"/>: derives the
///         "looked-at mirror" index that <see cref="FocusAndStalenessSelector"/>
///         hard-locks to when the player is moving and aiming at a
///         mirror, so the mirror they're using as a rear-view never
///         loses its slot to peer mirrors accumulating staleness.</item>
///   <item><see cref="PanelDebug"/>: anchors the HUD's "LOOKED AT"
///         detail block on whatever's actually under the crosshair,
///         independent of which group the picker chose this batch.</item>
/// </list></para>
///
/// <para>Geometry is plane-AABB intersection in the group's
/// (BasisU, BasisV) coords — same coordinate system the merge builder
/// uses, so a multi-member group is treated as the union rectangle
/// it visually presents rather than per-member checks.</para>
/// </summary>
internal static class CrosshairHit
{
    /// <summary>Index of the closest panel group under the crosshair,
    /// or -1 if the forward ray misses every group. Closest = smallest
    /// positive ray t.</summary>
    public static int FindIndex(RenderUnit[] units, int unitCount, MatrixD playerWorld)
    {
        Vector3D eye = playerWorld.Translation;
        Vector3D dir = playerWorld.Forward;
        dir.Normalize();

        int    best  = -1;
        double bestT = double.PositiveInfinity;

        for (int i = 0; i < unitCount; i++)
        {
            var g = units[i].Group;
            // Skip groups whose lead plane never resolved — no basis
            // to intersect against.
            if (g.Normal.LengthSquared() <= 0.5) continue;

            double denom = Vector3D.Dot(dir, g.Normal);
            if (Math.Abs(denom) < 1e-6) continue;  // parallel-ray guard

            double t = Vector3D.Dot(g.Origin - eye, g.Normal) / denom;
            if (t <= 0.0) continue;      // behind the eye
            if (t >= bestT) continue;    // already have a closer hit

            Vector3D p = eye + dir * t;
            Vector3D d = p - g.Origin;
            double u = Vector3D.Dot(d, g.BasisU);
            double v = Vector3D.Dot(d, g.BasisV);
            if (u < g.UMin || u > g.UMax) continue;
            if (v < g.VMin || v > g.VMax) continue;

            bestT = t;
            best  = i;
        }
        return best;
    }

    /// <summary>Index of the closest <b>mirror</b>-mode panel under the
    /// crosshair, or -1 if the ray misses every mirror. Cameras are
    /// ignored even if they're closer to the ray.
    ///
    /// <para>The hit rectangle is expanded by
    /// <see cref="MirrorEdgePadMeters"/> on each side so the hard-lock
    /// engages when the crosshair is "near" the mirror, not only when
    /// it's strictly inside the panel border. Without the pad, tiny
    /// hand-jitter while moving would dropping out of the lock and
    /// surrender the slot to a peer mirror for a frame, defeating the
    /// purpose.</para></summary>
    public static int FindMirrorIndex(RenderUnit[] units, int unitCount, MatrixD playerWorld)
    {
        Vector3D eye = playerWorld.Translation;
        Vector3D dir = playerWorld.Forward;
        dir.Normalize();

        int    best  = -1;
        double bestT = double.PositiveInfinity;

        for (int i = 0; i < unitCount; i++)
        {
            ref readonly RenderUnit u = ref units[i];
            if (u.Group.Members[0].Surface.Mode != PanelMode.Mirror) continue;

            var g = u.Group;
            if (g.Normal.LengthSquared() <= 0.5) continue;

            // Front-side check: the mirror's visible face must point
            // toward the eye, not away. Without this, back-to-back
            // mirrors (one facing the player, one facing away) both
            // pass the ray-plane intersection and the "wrong" one can
            // win on iteration order or t precision. Belt-and-braces
            // vs FacingCull — FacingCull operates on a per-cull-context
            // matrix that can lag behind the current eye, so re-check
            // here against the actual playerWorld.Translation we got.
            if (Vector3D.Dot(eye - g.Origin, g.Normal) <= 0) continue;

            double denom = Vector3D.Dot(dir, g.Normal);
            if (Math.Abs(denom) < 1e-6) continue;

            double t = Vector3D.Dot(g.Origin - eye, g.Normal) / denom;
            if (t <= 0.0) continue;
            if (t >= bestT) continue;

            Vector3D p = eye + dir * t;
            Vector3D d = p - g.Origin;
            double uc = Vector3D.Dot(d, g.BasisU);
            double vc = Vector3D.Dot(d, g.BasisV);
            if (uc < g.UMin - MirrorEdgePadMeters || uc > g.UMax + MirrorEdgePadMeters) continue;
            if (vc < g.VMin - MirrorEdgePadMeters || vc > g.VMax + MirrorEdgePadMeters) continue;

            bestT = t;
            best  = i;
        }
        return best;
    }

    // World-space pad (meters) added to each side of a mirror's UV
    // rectangle before the crosshair-in-bounds test. Generous enough to
    // tolerate sub-degree hand jitter while moving and to feel like
    // "still looking at the mirror" when the crosshair drifts just past
    // the frame; small enough that an adjacent mirror on the same wall
    // doesn't get double-counted. 0.10 m = 10 cm.
    private const double MirrorEdgePadMeters = 0.10;
}
