using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRage;
using VRage.Game.Entity;
using VRage.Render.Scene;
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="IFirstPersonHeadFix"/> implementation. Polls the
/// FPV state at a fixed cadence on the sim thread, publishes the
/// result via a volatile <see cref="Boxed{T}"/>, and on the render
/// thread clears <c>SkipInMainView | SkipInForward</c> on every
/// renderable proxy that belongs to a material disabled in first
/// person.
///
/// <para>No render-thread restore is needed: the engine's main view
/// pass re-masks the proxies every frame via
/// <c>EntityMaterialRenderFlagChanges</c>. Leaving them Visible until
/// the next mask pass is benign and actually fixes a subtle third-
/// person regression that explicit restore caused.</para>
/// </summary>
internal sealed class FirstPersonHeadFix : IFirstPersonHeadFix
{
    private const int DetectionInterval = 10;
    private uint _tickCounter;

    // Sim writes; render reads. Boxed<T> is a thin reference wrapper
    // so volatile assignment is an atomic reference swap regardless of
    // the wrapped tuple's size.
    private volatile Boxed<(uint CharacterActorId, string[] MaterialsDisabledInFirst)> _current;

    public void OnSimTick(bool enabled)
    {
        if (!enabled)
        {
            _current = null;
            return;
        }
        if (++_tickCounter % DetectionInterval != 0) return;

        // The local player's character is the right target whether
        // they're walking in FPV or sitting in a cockpit. In the
        // cockpit case the camera controller is the cockpit, not the
        // character — the earlier "is MyCharacter" check missed it
        // entirely, so panel renders showed no head when the player
        // was seated.
        var character = MySession.Static?.LocalCharacter;
        if (character == null) { _current = null; return; }

        // Head mask is active in two situations:
        //   1. Walking: character itself is in FPV.
        //   2. Seated: the cockpit the character is piloting is in
        //      FPV. The cockpit's OnAssumeControl / view-toggle path
        //      calls Pilot.EnableHead(!FPV), which applies the SAME
        //      MaterialsDisabledIn1st mask used in walking-FPV.
        // We only need to fight the engine's mask when it's actually
        // applied — otherwise leaving Visible flags untouched is fine
        // and avoids needless render-thread work.
        bool masked = character.IsInFirstPersonView || character.ForceFirstPersonCamera;
        if (!masked
            && MySession.Static?.CameraController is MyCockpit cockpit
            && ReferenceEquals(cockpit.Pilot, character)
            && (cockpit.IsInFirstPersonView || cockpit.ForceFirstPersonCamera))
        {
            masked = true;
        }
        if (!masked) { _current = null; return; }

        // Cast through MyEntity so the compiler emits a virtual call to
        // the public base property MyEntity.get_Render() rather than
        // MyCharacter's `internal new Render` override. Workaround for
        // Pulsar's source-compiler load-context not honoring
        // Publicizer's IgnoresAccessChecksTo for the override.
        uint renderObjectId = ((MyEntity)character).Render.GetRenderObjectID();
        string[] materials  = character.Definition.MaterialsDisabledIn1st;
        _current = new Boxed<(uint, string[])>((renderObjectId, materials));
    }

    public void ApplyDuringRender()
    {
        var info = _current;  // single volatile read; consistent snapshot
        if (info == null) return;

        var actor = MyIDTracker<MyActor>.FindByID(info.BoxedValue.CharacterActorId);
        if (actor == null) return;

        var renderable = actor.GetComponent<MyRenderableComponent>();
        if (renderable == null) return;

        var lods = renderable.Lods;
        var mesh = renderable.Mesh;
        if (lods == null || mesh == null) return;

        string[] disabledMaterials = info.BoxedValue.MaterialsDisabledInFirst;
        // NPC + some modded character definitions leave MaterialsDisabledIn1st
        // null. Engine guards this in MyCharacter.UpdateHeadModelProperties;
        // we must too, or this would NRE inside the render-thread loop and
        // the outer try/catch in the pipeline would swallow it as a render
        // failure, killing every panel for the duration of FPV with that
        // character.
        if (disabledMaterials == null) return;

        // CLEAR the skip-bits rather than OR'ing in Visible: the engine
        // gates the SkipInMainView | SkipInForward addition on
        // !flags.HasFlags(Visible) (see decompiled MyProxiesFactory).
        // For an already-skipped proxy, GetRenderableProxyFlags(Visible)
        // returns None, so OR'ing it would do nothing.
        const MyRenderableProxyFlags skipMask =
            MyRenderableProxyFlags.SkipInMainView | MyRenderableProxyFlags.SkipInForward;

        // Tight loop: index-based to avoid IEnumerator boxing on the
        // hot path. Disabled-materials arrays are small (typical < 8).
        for (int m = 0; m < disabledMaterials.Length; m++)
        {
            MyStringId material = MyStringId.GetOrCompute(disabledMaterials[m]);
            for (int j = 0; j < lods.Length; j++)
            {
                var lod = lods[j];
                var proxies = lod.RenderableProxies;
                for (int k = 0; k < proxies.Length; k++)
                {
                    var proxy = proxies[k];
                    if (MyMeshes.GetMeshPart(mesh, j, proxy.PartIndex)
                            .Info.Material.Info.Name == material)
                        proxy.Flags &= ~skipMask;
                }
            }
        }
    }
}
