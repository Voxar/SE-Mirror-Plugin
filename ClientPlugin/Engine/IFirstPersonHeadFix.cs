namespace ClientPlugin;

/// <summary>
/// First-person head/eye visibility fix. In FPV the engine masks the
/// local character's head materials with
/// <c>SkipInMainView | SkipInForward</c> so the player doesn't see the
/// inside of their own face. For mirror/camera panel renders we want
/// the head visible so the reflection shows a complete character.
///
/// <para>Sim thread polls the camera-controller state via
/// <see cref="OnSimTick"/>. Render thread calls
/// <see cref="ApplyDuringRender"/> from within the panel render pass
/// to clear the skip-flags on the relevant proxies. The engine re-
/// masks the proxies during its main view pass each frame, so no
/// explicit restore is required.</para>
/// </summary>
internal interface IFirstPersonHeadFix
{
    /// <summary>Sim-thread tick. Updates the cached FPV character info
    /// (volatile-published so the render thread sees consistent reads).
    /// When <paramref name="enabled"/> is false, the cached info is
    /// cleared so the render thread never applies the fix.</summary>
    void OnSimTick(bool enabled);

    /// <summary>Render-thread call from inside the panel render pass.
    /// If FPV is currently active, clears
    /// <c>SkipInMainView | SkipInForward</c> on every disabled-in-1st-
    /// person material's renderable proxy.</summary>
    void ApplyDuringRender();
}
