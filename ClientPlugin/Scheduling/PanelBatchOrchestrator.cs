using System;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Default <see cref="IPanelBatchOrchestrator"/>. Pre-allocates every
/// per-batch buffer (units array, picked flags, cached BoundingFrustum)
/// so the render loop generates zero managed allocations in steady
/// state.
/// </summary>
internal sealed class PanelBatchOrchestrator : IPanelBatchOrchestrator
{
    /// <summary>cos(angle) lower bound for the look-direction cull.
    /// 0.26 ≈ cos(75°) — a peripheral mirror at 74° passes, at 76°
    /// it doesn't.</summary>
    private const double LookCosThreshold = 0.26;

    /// <summary>Squared translation (in m²) below which the player is
    /// treated as stationary between batches. 1 cm² ≈ 1cm displacement
    /// per batch — above cockpit-bobbing / float jitter, below the
    /// slowest meaningful walk.</summary>
    private const double MovingDistSqThreshold = 0.0001;

    /// <summary>Minimum on-screen coverage (fraction of main view) below
    /// which a unit is culled before scheduling. 1e-4 = 0.01% of screen ≈
    /// ~14×14 pixels on 1080p — well below the size at which an LCD's
    /// content is readable, and well below the size at which SE bothers
    /// to allocate the block's offscreen RT. Catches the "tiny far
    /// panel that picks-and-fails forever via staleness runaway" case.</summary>
    private const double MinCoverage = 1e-4;


    /// <summary>Multiplier on the user's configured panel far-clip
    /// distance for slot 1+ renders. Secondary panels are out of the
    /// player's direct gaze; cutting their far plane in half saves
    /// distant LOD / cascaded-shadow work without visible quality
    /// loss at typical viewing distances. Temporarily 1.0 (no
    /// reduction) while we isolate whether the reflection FOV bug
    /// follows the resolution override or this factor.</summary>
    private const float SecondarySlotFarPlaneFactor = 1.0f;

    private readonly ISurfaceRegistry           _registry;
    private readonly IPanelGroupBuilder        _groupBuilder;
    private readonly IPanelGroupPlaneRefresher _planeRefresher;
    private readonly IUnitScorer                _scorer;
    private readonly PanelCullChain             _cullChain;
    private readonly IPanelSlotSelector         _slot0Selector;
    private readonly IPanelSlotSelector         _slot1PlusSelector;
    private readonly IPanelRenderer             _panelDispatcher;
    private readonly IMirrorGroupRenderer       _groupRenderer;
    private readonly ILcdOffscreenResolver      _offscreenResolver;
    private readonly LcdRtBucketPolicy          _bucketPolicy;
    private readonly IPanelStatusSink           _statusSink;
    private readonly IMirrorPluginSettings      _settings;
    private readonly ThrottledDiagLog           _diag;

    // ── Pre-allocated per-batch buffers (render thread only) ────────

    private RenderUnit[] _units       = new RenderUnit[32];
    private bool[]       _pickedFlags = new bool[16];
    private readonly BoundingFrustumD _cachedFrustum = new BoundingFrustumD();

    private long _tickCounter;

    // Player translation tracking — used to tell the selector whether
    // the reflected-eye position is moving (which is when mirror
    // ghosting manifests; rotation alone doesn't ghost).
    private Vector3D _prevPlayerPos;
    private bool     _hasPrevPlayerPos;

    public PanelBatchOrchestrator(
        ISurfaceRegistry           registry,
        IPanelGroupBuilder        groupBuilder,
        IPanelGroupPlaneRefresher planeRefresher,
        IUnitScorer                scorer,
        PanelCullChain             cullChain,
        IPanelSlotSelector         slot0Selector,
        IPanelSlotSelector         slot1PlusSelector,
        IPanelRenderer             panelDispatcher,
        IMirrorGroupRenderer       groupRenderer,
        ILcdOffscreenResolver      offscreenResolver,
        LcdRtBucketPolicy          bucketPolicy,
        IPanelStatusSink           statusSink,
        IMirrorPluginSettings      settings,
        ThrottledDiagLog           diag)
    {
        _registry          = registry          ?? throw new ArgumentNullException(nameof(registry));
        _groupBuilder      = groupBuilder      ?? throw new ArgumentNullException(nameof(groupBuilder));
        _planeRefresher    = planeRefresher    ?? throw new ArgumentNullException(nameof(planeRefresher));
        _scorer            = scorer            ?? throw new ArgumentNullException(nameof(scorer));
        _cullChain         = cullChain         ?? throw new ArgumentNullException(nameof(cullChain));
        _slot0Selector     = slot0Selector     ?? throw new ArgumentNullException(nameof(slot0Selector));
        _slot1PlusSelector = slot1PlusSelector ?? throw new ArgumentNullException(nameof(slot1PlusSelector));
        _panelDispatcher   = panelDispatcher   ?? throw new ArgumentNullException(nameof(panelDispatcher));
        _groupRenderer     = groupRenderer     ?? throw new ArgumentNullException(nameof(groupRenderer));
        _offscreenResolver = offscreenResolver ?? throw new ArgumentNullException(nameof(offscreenResolver));
        _bucketPolicy      = bucketPolicy      ?? throw new ArgumentNullException(nameof(bucketPolicy));
        _statusSink        = statusSink        ?? throw new ArgumentNullException(nameof(statusSink));
        _settings          = settings          ?? throw new ArgumentNullException(nameof(settings));
        _diag              = diag              ?? throw new ArgumentNullException(nameof(diag));
    }

    public void RunBatch()
    {
        if (!_settings.Enabled)
        {
            // Tell every registered panel the plugin is intentionally
            // off, so the mod TSS splash shows "Plugin disabled"
            // instead of the default "Plugin not loaded". The status
            // sink dedupes against LastReportedStatus, so steady-state
            // cost is one string compare per panel per frame.
            var disabled = _registry.SnapshotForRender();
            for (int i = 0; i < disabled.Length; i++)
                _statusSink.Report(disabled[i], "Plugin disabled");
            return;
        }

        // Panels strain the GPU — let go whenever the player isn't
        // actively in-game. Covers Esc menu in single-player (IsPaused),
        // any pause source, and main-menu / world-not-loaded state
        // (Session null). In MP we keep rendering because the world
        // is still simulating regardless of the player's menu state.
        // RenderOnPauseScreen overrides the pause gate for users who
        // want the panels to keep going while they're staring at the
        // menu (mostly useful for screenshots / showing off builds).
        if (Sandbox.MySandboxGame.IsPaused && !_settings.RenderOnPauseScreen) return;
        if (Sandbox.ModAPI.MyAPIGateway.Session == null) return;

        // Bail in VR/stereo mode: SetupCameraMatrices would apply our
        // panel matrices to BOTH per-eye envMatrices and corrupt main
        // view's stereo state.
        try { if (MyStereoRender.Enable) return; } catch { }

        _tickCounter++;
        _diag.AdvanceTick();

        MatrixD playerWorld;
        MatrixD viewProjection;
        try
        {
            var em = MyRender11.Environment.Matrices;
            playerWorld    = em.InvViewD;
            viewProjection = em.ViewProjectionD;
        }
        catch (Exception ex) { _diag.Log("view", "no valid view: " + ex.Message); return; }

        // Player translation delta — selector uses this to relax the
        // focused-mirror auto-win when the eye isn't moving. Default
        // to "moving" on the very first batch so we don't surprise-
        // skip a focused-mirror render before we've sampled twice.
        bool isPlayerMoving = true;
        if (_hasPrevPlayerPos)
        {
            double d2 = Vector3D.DistanceSquared(playerWorld.Translation, _prevPlayerPos);
            isPlayerMoving = d2 > MovingDistSqThreshold;
        }
        _prevPlayerPos    = playerWorld.Translation;
        _hasPrevPlayerPos = true;

        // "Player is in a cockpit / seat / control station": the entity
        // they're controlling is a ship controller (covers regular
        // cockpits, passenger seats, remote control, cryo chambers,
        // ...). Selector uses this to ignore focus score — view
        // direction inside a vehicle is dictated by the vehicle, not
        // the player aiming AT a panel.
        bool isPlayerInCockpit = false;
        try
        {
            var controlled = Sandbox.ModAPI.MyAPIGateway.Session?.ControlledObject;
            isPlayerInCockpit = controlled is Sandbox.ModAPI.Ingame.IMyShipController;
        }
        catch { /* engine not ready / no session — treat as not-in-cockpit */ }

        CaptureViewFrustum(out var viewFrustum);

        // 1. Group + plane refresh.
        var groups = _groupBuilder.GetGroups(_registry);
        if (groups.Count == 0) return;
        _planeRefresher.Refresh(groups);

        // 2. Score.
        if (_units.Length < groups.Count) _units = new RenderUnit[Math.Max(groups.Count, _units.Length * 2)];
        int unitCount = _scorer.Score(groups, playerWorld, viewProjection, _units);

        // 3. Cull.
        var cullCtx = new CullContext(
            eye: playerWorld.Translation,
            forward: NormalizedForward(playerWorld),
            viewFrustum: viewFrustum,
            lookCosThreshold: LookCosThreshold);

        unitCount = CullInPlace(unitCount, in cullCtx);

        // Panel-vs-panel occlusion: drop units whose screen-projected
        // AABB is fully contained inside a closer unit's AABB.
        unitCount = OcclusionCullPanelToPanel(unitCount);

        if (unitCount == 0) return;

        // 4. Pick + render. Capture main state ONCE, restore ONCE at
        // batch end (per-panel restore happens inside each renderer's
        // MainCameraStateGuard).
        if (_pickedFlags.Length < unitCount) _pickedFlags = new bool[Math.Max(unitCount, _pickedFlags.Length * 2)];
        Array.Clear(_pickedFlags, 0, unitCount);

        var mainState = PanelRenderState.CaptureMain();

        int cap = Math.Max(1, _settings.MaxPerFrame);

        // Looked-at mirror under the crosshair (ray-vs-plane). When the
        // player is moving, FocusAndStalenessSelector locks to this
        // index every frame so the active rear-view mirror never loses
        // a slot to a peer's accumulated staleness.
        int lookedAtMirrorIdx = CrosshairHit.FindMirrorIndex(_units, unitCount, playerWorld);

        try
        {
            for (int slot = 0; slot < cap; slot++)
            {
                int idx = (slot == 0)
                    ? _slot0Selector.PickNext(_units, unitCount, _pickedFlags, _tickCounter, isPlayerMoving, isPlayerInCockpit, lookedAtMirrorIdx)
                    : _slot1PlusSelector.PickNext(_units, unitCount, _pickedFlags, _tickCounter, isPlayerMoving, isPlayerInCockpit, lookedAtMirrorIdx);
                if (idx < 0) break;
                _pickedFlags[idx] = true;

                // Per-slot context: far plane halved for slot 1+, slot
                // index propagated to renderers so they can pick the
                // right scale and the HUD can label which slot picked
                // each unit. PanelFarClipM is upper-capped to the main
                // view's far clip — no point in panels claiming to see
                // farther than the player can. FarFarPlane mirrors the
                // main view's LargeDistanceFarClipping so distant
                // impostors / Ansel math stays consistent across views.
                float mainFar    = (float)MyRender11.Environment.Matrices.FarClipping;
                float mainFarFar = MyRender11.Environment.Matrices.LargeDistanceFarClipping;
                float baseFar    = Math.Min(_settings.PanelFarClipM, mainFar);
                float farM = slot == 0
                    ? baseFar
                    : baseFar * SecondarySlotFarPlaneFactor;
                var ctx = new PanelRenderContext(
                    in mainState, playerWorld, _tickCounter, slot, farM, mainFarFar);

                RenderUnit(in _units[idx], in ctx);
            }

            // Debug HUD overlay — no-op when the setting is off.
            // Drawn AFTER picks so the picked-flag array reflects this
            // batch's selections.
            PanelDebug.DrawHud(_units, unitCount, _pickedFlags, _tickCounter, playerWorld,
                               isPlayerMoving, isPlayerInCockpit);
        }
        finally
        {
            // Final restore. Inner MainCameraStateGuard already
            // restored after each render, so this is belt-and-braces
            // — but it ensures restore even if the slot loop itself
            // threw between renders.
            mainState.Apply();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private void CaptureViewFrustum(out BoundingFrustumD frustum)
    {
        frustum = null;
        try
        {
            var live = MyRender11.Environment.Matrices.ViewFrustumClippedD;
            if (live == null) return;
            // Build into our cached instance; setting the Matrix
            // property recomputes the plane equations in place.
            // Reusing the same instance avoids per-batch allocation.
            _cachedFrustum.Matrix = live.Matrix;
            frustum = _cachedFrustum;
        }
        catch { /* fall back to no frustum cull */ }
    }

    /// <summary>Panel-vs-panel occlusion: drop any unit whose
    /// screen-projected NDC AABB is fully contained inside a closer
    /// unit's NDC AABB (both clipped to the viewport). O(N²) over
    /// surviving units; cheap enough at typical N (~30) — sub-microsecond.
    ///
    /// <para>Conservative: only catches FULL occlusion by a single
    /// closer panel. Partial cover by multiple closer panels won't
    /// trigger. False positives possible when a closer panel's AABB
    /// projection overestimates its actual shape (oblique panels) —
    /// acceptable tradeoff given the speedup of skipping the render.</para></summary>
    private int OcclusionCullPanelToPanel(int unitCount)
    {
        if (unitCount <= 1) return unitCount;

        // Mark phase: which units are occluded.
        // Reuse _pickedFlags as a temp buffer (it gets cleared before pick).
        if (_pickedFlags.Length < unitCount)
            _pickedFlags = new bool[Math.Max(unitCount, _pickedFlags.Length * 2)];
        Array.Clear(_pickedFlags, 0, unitCount);

        for (int b = 0; b < unitCount; b++)
        {
            // B's quad must be valid to test occlusion of it (if it
            // has a corner past the near plane, we can't know what
            // shape it covers — be safe and keep it).
            if (!_units[b].NdcQuadValid) continue;

            for (int a = 0; a < unitCount; a++)
            {
                if (a == b) continue;
                if (_units[a].DistSq >= _units[b].DistSq) continue;  // A must be closer
                // A's quad must be valid to use as occluder. Skipping
                // close-to-camera panels here is the fix for the
                // "standing inside a mirror wall culls everything else"
                // case — that panel's quad isn't reliably computable,
                // so we don't trust it to occlude anything.
                if (!_units[a].NdcQuadValid) continue;

                // Cheap AABB pre-reject: A's quad ⊆ A's AABB, so if
                // A's AABB doesn't contain B's AABB, A's quad can't
                // contain B's quad either. Filters out the vast
                // majority of pairs at 4 comparisons each, before
                // paying the 16-cross-product quad test.
                if (_units[a].NdcMin.X > _units[b].NdcMin.X) continue;
                if (_units[a].NdcMax.X < _units[b].NdcMax.X) continue;
                if (_units[a].NdcMin.Y > _units[b].NdcMin.Y) continue;
                if (_units[a].NdcMax.Y < _units[b].NdcMax.Y) continue;

                if (QuadContainsAllCorners(in _units[a], in _units[b]))
                {
                    _pickedFlags[b] = true;
                    break;
                }
            }
        }

        // Compact phase: drop marked.
        int dst = 0;
        for (int src = 0; src < unitCount; src++)
        {
            if (_pickedFlags[src]) continue;
            if (dst != src) _units[dst] = _units[src];
            dst++;
        }
        return dst;
    }

    /// <summary>True iff every one of <paramref name="inner"/>'s four
    /// projected NDC corners lies inside <paramref name="outer"/>'s
    /// projected NDC convex quad. CCW winding assumed (front-facing
    /// panels survive the facing cull, which guarantees the projected
    /// quad is CCW in NDC).
    ///
    /// <para>Half-plane test per edge: for each directed edge
    /// <c>E0→E1</c> of the outer quad, a point P is on the "inside"
    /// (left) iff the 2D cross product
    /// <c>(E1-E0) × (P-E0)</c> ≥ 0. Inner is contained iff every
    /// corner passes the test for all four edges. 16 cross products
    /// per pair — sub-µs at typical N.</para></summary>
    private static bool QuadContainsAllCorners(in RenderUnit outer, in RenderUnit inner)
    {
        return PointInsideQuad(in outer, inner.NdcC0)
            && PointInsideQuad(in outer, inner.NdcC1)
            && PointInsideQuad(in outer, inner.NdcC2)
            && PointInsideQuad(in outer, inner.NdcC3);
    }

    private static bool PointInsideQuad(in RenderUnit quad, Vector2D p)
        => IsLeftOf(quad.NdcC0, quad.NdcC1, p)
        && IsLeftOf(quad.NdcC1, quad.NdcC2, p)
        && IsLeftOf(quad.NdcC2, quad.NdcC3, p)
        && IsLeftOf(quad.NdcC3, quad.NdcC0, p);

    /// <summary>2D cross product sign — true if <paramref name="p"/>
    /// is on the left side of the directed edge
    /// <paramref name="a"/>→<paramref name="b"/> (or exactly on it).
    /// Slightly tolerant of degenerate edges via the &gt;=0 test.</summary>
    private static bool IsLeftOf(Vector2D a, Vector2D b, Vector2D p)
    {
        double cz = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
        return cz >= 0;
    }

    /// <summary>Cull-in-place: walk the units array, drop rejects by
    /// swapping the last surviving unit into the dropped slot. Returns
    /// the new valid count.</summary>
    private int CullInPlace(int unitCount, in CullContext ctx)
    {
        int dst = 0;
        for (int src = 0; src < unitCount; src++)
        {
            // Coverage cull (cheap, runs first): drop units too small
            // on screen to be worth rendering. Stops the staleness
            // runaway loop where a tiny far panel that fails to render
            // keeps accumulating priority and dominating the picker.
            if (_units[src].Coverage < MinCoverage) continue;
            if (_cullChain.ShouldKeep(_units[src].Group, in ctx))
            {
                if (dst != src) _units[dst] = _units[src];
                dst++;
            }
        }
        return dst;
    }

    private bool RenderUnit(in RenderUnit unit, in PanelRenderContext ctx)
    {
        // Render-resolution scale: when enabled, snap the scene
        // viewport to the LCD's offscreen RT size (bucketed to
        // {128, 256, 512, 1024} per-axis). Result:
        //  - Always pure downsample or 1:1 in the final blit — no
        //    upscale (upscale was the source of the "mirror FOV
        //    zoom" at small scales).
        //  - Aspect ratio matches the LCD's offscreen, so non-square
        //    LCDs keep their proportions in the off-axis projection.
        //  - Borrow-pool unique-size count bounded by 16 (per-axis
        //    buckets × per-axis buckets) — fits comfortably in the
        //    engine's pool ceiling.
        // Disposed at end-of-scope restores the resolution globals
        // even if the render throws.
        Vector2I vpSize = MyRender11.ResolutionI;        // default: main view
        if (_settings.DistanceResolutionScale)
        {
            var leadSurface = unit.Group.Members[0].Surface;
            if (_offscreenResolver.TryResolve(leadSurface.Block, leadSurface.SurfaceIdx,
                                              out var lcdInfo) && lcdInfo.Rtv != null)
            {
                vpSize = _bucketPolicy.ResolutionFor(
                    lcdSize:     lcdInfo.Rtv.Size,
                    coverage:    unit.Coverage,
                    lookFactor:  unit.LookFactor,
                    mainViewCap: MyRender11.ResolutionI);
            }
        }

        using (RenderResolutionGuard.Push(vpSize))
        {
            var group = unit.Group;
            if (group.IsSolo)
            {
                var s = group.Members[0].Surface;
                bool ok = _panelDispatcher.Render(s, in ctx);
                if (ok) { s.MarkRendered(_tickCounter); _statusSink.Report(s, "rendered"); }
                else
                {
                    // Advance staleness clock even on failure — otherwise
                    // a permanently-broken panel (e.g. far LCD whose
                    // offscreen RT isn't allocated) grows unbounded
                    // staleness and dominates the picker forever.
                    s.MarkAttemptFailed(_tickCounter);
                    _statusSink.Report(s, "failed: " + (s.LastFailure ?? "unknown"));
                    _diag.Log("renderfail",
                        $"block={s.Block?.EntityId} subtype={s.Block?.BlockDefinition.SubtypeName} " +
                        $"surf={s.SurfaceIdx} mode={s.Mode} cov={unit.Coverage:F4} " +
                        $"dist={Math.Sqrt(unit.DistSq):F2}m reason='{s.LastFailure ?? "unknown"}'");
                }
                return ok;
            }
            else
            {
                bool ok = _groupRenderer.Render(group, in ctx);
                var members = group.Members;
                int n = members.Count;
                if (ok)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var m = members[i].Surface;
                        m.MarkRendered(_tickCounter);
                        _statusSink.Report(m, "rendered");
                    }
                }
                else
                {
                    for (int i = 0; i < n; i++)
                    {
                        var m = members[i].Surface;
                        m.MarkAttemptFailed(_tickCounter);
                        _statusSink.Report(m, "failed: " + (m.LastFailure ?? "group render failed"));
                    }
                    var lead = members[0].Surface;
                    _diag.Log("renderfail",
                        $"group n={n} lead={lead.Block?.EntityId} subtype={lead.Block?.BlockDefinition.SubtypeName} " +
                        $"mode={lead.Mode} cov={unit.Coverage:F4} dist={Math.Sqrt(unit.DistSq):F2}m " +
                        $"reason='{lead.LastFailure ?? "group render failed"}'");
                }
                return ok;
            }
        }
    }

    private static Vector3D NormalizedForward(MatrixD playerWorld)
    {
        Vector3D f = playerWorld.Forward;
        f.Normalize();
        return f;
    }
}
