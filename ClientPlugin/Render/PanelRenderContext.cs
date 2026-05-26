using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Per-slot inputs to every <see cref="IPanelRenderer"/> call. The
/// orchestrator builds one of these per slot inside a batch — the
/// batch-scoped fields (<see cref="MainState"/>, <see cref="ViewerWorld"/>,
/// <see cref="TickCounter"/>) are the same across the slot loop, but
/// the slot-scoped fields (<see cref="Slot"/>,
/// <see cref="EffectiveFarPlaneM"/>) differ.
/// </summary>
internal readonly struct PanelRenderContext
{
    /// <summary>Main view's captured render state. Renderers derive
    /// their panel-specific variant from this snapshot via
    /// <see cref="PanelRenderState.ForMirror"/> /
    /// <see cref="PanelRenderState.ForCamera"/>.</summary>
    public readonly PanelRenderState MainState;

    /// <summary>World matrix of the player's eye / main camera at
    /// batch start. <c>Translation</c> is the eye position; rows give
    /// the basis (Right/Up/Forward).</summary>
    public readonly MatrixD ViewerWorld;

    /// <summary>Tick counter for this batch. Renderers don't read it;
    /// passed through so the orchestrator can mark surfaces rendered
    /// with the same tick value used for staleness scoring.</summary>
    public readonly long TickCounter;

    /// <summary>Which batch slot this render is filling. 0 is the
    /// focused / primary pick; 1+ are filler picks behind the player's
    /// direct gaze that tolerate coarser quality settings.</summary>
    public readonly int Slot;

    /// <summary>Far clip plane to use for this render. Slot 0 is
    /// <c>min(PanelFarClipM, main view's far clipping)</c>; slot 1+
    /// is <c>* SecondarySlotFarPlaneFactor</c>. The min-with-main-view
    /// cap stops panel renders from claiming to see farther than the
    /// main view does — pointless work since the player can't actually
    /// see any further than that themselves.</summary>
    public readonly float EffectiveFarPlaneM;

    /// <summary>Far-far plane (large-distance clipping). Always the
    /// main view's <c>LargeDistanceFarClipping</c> — the panel render
    /// should use the same large-distance threshold as the player's
    /// own view so distant impostors / Ansel screenshot math behave
    /// consistently. NOT slot-adjusted.</summary>
    public readonly float EffectiveFarFarPlaneM;

    public PanelRenderContext(in PanelRenderState mainState,
                              MatrixD viewerWorld, long tickCounter,
                              int slot, float effectiveFarPlaneM,
                              float effectiveFarFarPlaneM)
    {
        MainState             = mainState;
        ViewerWorld           = viewerWorld;
        TickCounter           = tickCounter;
        Slot                  = slot;
        EffectiveFarPlaneM    = effectiveFarPlaneM;
        EffectiveFarFarPlaneM = effectiveFarFarPlaneM;
    }
}
