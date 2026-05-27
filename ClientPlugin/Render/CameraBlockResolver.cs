using System;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRageMath;
using IMyCameraBlock     = Sandbox.ModAPI.IMyCameraBlock;
using IMyCubeBlock       = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeBlockIngame = VRage.Game.ModAPI.Ingame.IMyCubeBlock;
using IMyFunctionalBlock = Sandbox.ModAPI.IMyFunctionalBlock;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="CameraBlockResolver"/>. The mod hands us the
/// camera-block reference directly via the bridge (<see cref="PanelConfig.CameraBlock"/>),
/// so this resolver no longer does any per-frame entity-table lookup
/// — it just validates the reference still represents a usable block
/// (not closed, working, functional) and produces the view matrix
/// + post-zoom FOV.
///
/// <para>The previous lookup-by-id path had a narrow but real hole
/// where the engine's per-thread vs main-thread entity tables could
/// briefly disagree, or where the entity's <c>Closed</c> flag
/// transiently flipped during save sync — both manifesting as
/// "camera lookup failed" forever on a perfectly fine camera.
/// Reference-in-hand removes that class of failure entirely.</para>
/// </summary>
internal sealed class CameraBlockResolver
{
    // Mirror of MirrorCameraMod.Settings.SurfaceSettings's zoom range.
    // Plugin can't reference the mod assembly, so these constants live
    // in both places — keep them in sync when either side bumps.
    private const float MinZoom = 1.0f;
    private const float MaxZoom = 20.0f;

    // 70° in radians. Used as fallback when m_fov is unreadable
    // (modded camera blocks that don't derive from MyCameraBlock).
    private const float FallbackFovRad = 1.221730f;

    private readonly ActorMatrixSource _actorMatrix;

    public CameraBlockResolver(ActorMatrixSource actorMatrix)
    {
        _actorMatrix = actorMatrix ?? throw new ArgumentNullException(nameof(actorMatrix));
    }

    public bool TryResolve(IMyCubeBlockIngame cameraBlock, float zoom,
                           out CameraBlockView view, out string failureReason)
    {
        view = default;
        failureReason = null;

        if (cameraBlock == null)
        {
            failureReason = "no camera selected";
            return false;
        }

        // The reference came in via the bridge. Validate it's still a
        // usable camera-like block — the mod registered it, but state
        // can change between registration and render.
        if (!(cameraBlock is IMyCameraBlock))
        {
            failureReason = "block " + cameraBlock.EntityId.ToString("X")
                          + " is " + cameraBlock.GetType().Name + ", not a camera";
            return false;
        }
        if (!(cameraBlock is MyEntity ent))
        {
            failureReason = "block " + cameraBlock.EntityId.ToString("X")
                          + " is not a MyEntity";
            return false;
        }
        if (ent.Closed || ent.MarkedForClose)
        {
            failureReason = "camera " + ent.EntityId.ToString("X") + " is closed";
            return false;
        }

        if (cameraBlock is IMyFunctionalBlock func && !func.IsWorking)
        {
            failureReason = "camera not working (powered off or disabled)";
            return false;
        }
        if (cameraBlock is IMyCubeBlock cube && !cube.IsFunctional)
        {
            failureReason = "camera not functional (damaged or incomplete)";
            return false;
        }

        // Render-side matrix for jitter-free view (see
        // ActorMatrixSource doc). Apply the +0.2 m forward shift
        // AFTER the matrix is fetched so we don't accidentally mutate
        // the actor's cached state.
        MatrixD world = _actorMatrix.GetFreshestMatrix(ent);
        world.Translation = world.Translation + world.Forward * 0.2;

        // Mirror MyCameraBlock.GetViewMatrix: if the model defines a
        // "camera" dummy, its translation is the lens-vs-block offset.
        // SE applies the dummy's translation rotated into world space
        // via Quaternion.CreateFromForwardUp(Forward, Up). Without
        // this, modded cameras (and some vanilla variants) render
        // from the block centre instead of the actual lens position,
        // shifting the frustum origin and clipping geometry near
        // edges that should be visible.
        var model = ent.Render?.ModelStorage as VRage.Game.Models.MyModel;
        if (model?.Dummies != null)
        {
            foreach (var kv in model.Dummies)
            {
                if (kv.Value.Name == "camera")
                {
                    var rot = Quaternion.CreateFromForwardUp(world.Forward, world.Up);
                    world.Translation += MatrixD.Transform(kv.Value.Matrix, rot).Translation;
                    break;
                }
            }
        }

        // Use the camera's DEFINITION default FOV (widest, "unzoomed")
        // as our base — verified in-game that reading the live m_fov
        // stacks our zoom on top of the player's in-game camera zoom.
        // MaxFov stays stable regardless of what the player did with
        // mouse wheel while viewing the camera.
        float baseFov = FallbackFovRad;
        if (cameraBlock is MyCameraBlock vanilla)
        {
            baseFov = vanilla.BlockDefinition.MaxFov;
        }
        if (baseFov <= 0f || baseFov >= (float)Math.PI) baseFov = FallbackFovRad;

        if (zoom < MinZoom) zoom = MinZoom;
        if (zoom > MaxZoom) zoom = MaxZoom;
        float fovV = baseFov / zoom;

        view = new CameraBlockView(world, fovV);
        return true;
    }
}
