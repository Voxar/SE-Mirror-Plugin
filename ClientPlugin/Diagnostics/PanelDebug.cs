using System;
using VRage.Render11.Common;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace ClientPlugin;

/// <summary>
/// Plugin-wide debug utilities. Two responsibilities, one class
/// because they share the "debug" lifecycle (toggle via
/// <see cref="IMirrorPluginSettings.DebugHud"/> for HUD, always-on
/// for logging) and the same single-process render-thread caller:
///
/// <list type="bullet">
///   <item><b>HUD overlay</b>: <see cref="DrawHud"/> renders the top-N
///         scored render units to the engine's debug-text layer,
///         flagging which ones the orchestrator picked this batch.</item>
///   <item><b>Logging</b>: <see cref="Log"/> / <see cref="LogException"/>
///         centralize writes to <c>MyLog.Default</c> with a consistent
///         <c>[Mirror]</c> prefix. Replaces ad-hoc
///         <c>MyLog.Default.WriteLine("[Mirror] " + ...)</c>
///         scattered across the codebase.</item>
/// </list>
///
/// <para>Static because the only HUD caller —
/// <see cref="PanelBatchOrchestrator"/> — runs on the render thread,
/// the engine debug-draw API is render-thread, and there's exactly
/// one overlay per process. Logging methods are thread-safe via
/// <c>MyLog.Default</c>'s own synchronization.</para>
/// </summary>
internal static class PanelDebug
{
    // ── HUD layout ────────────────────────────────────────────────────

    private const int    HudMaxRows     = 10;
    private const float  HudTextScale   = 0.6f;
    private const float  HudRowHeightPx = 16f;

    // "Monospace" font ID resolved once. SE ships this font (defined
    // in Content/Data/Fonts.sbc) and it's registered into the render
    // font table at startup. (int) cast of the MyStringHash gives the
    // value the render side stored under.
    private static int s_monospaceFontIndex = -1;

    /// <summary>
    /// Draw a HUD text line in the built-in "Monospace" font via the
    /// engine's debug-overlay sprite queue. Bypasses
    /// <see cref="VRage.Render11.Sprites.MySpritesManager.AddMessage"/>
    /// (which shares the <c>DefaultOffscreenTarget</c> queue with the
    /// game HUD/menus and wipes the whole queue on frameId mismatch);
    /// instead writes directly to <c>SpritesManager.DebugDrawMessages</c>
    /// with <c>TargetTexture = "DEBUG_TARGET"</c>, mirroring exactly
    /// what <see cref="VRage.Render11.Sprites.MyDebugTextHelpers.DrawText"/>
    /// does internally — except the font is monospace, so columns line
    /// up across rows.
    /// </summary>
    private static void DrawHudText(Vector2 pos, string text, Color color)
    {
        if (s_monospaceFontIndex < 0)
            s_monospaceFontIndex = (int)MyStringHash.GetOrCompute("Monospace");

        var msg = MyRenderProxy.MessagePool.Get<MyRenderMessageDrawString>(MyRenderMessageEnum.DrawString);
        msg.Text           = text;
        msg.FontIndex      = s_monospaceFontIndex;
        msg.ScreenCoord    = pos;
        msg.ColorMask      = color;
        msg.ScreenScale    = HudTextScale;
        msg.ScreenMaxWidth = float.PositiveInfinity;
        msg.TargetTexture  = "DEBUG_TARGET";
        msg.IgnoreBounds   = true;

        MyManagers.SpritesManager.DebugDrawMessages.Messages.Add(msg);
    }
    private static readonly Vector2 HudOrigin      = new Vector2(20f, 60f);
    private static readonly Color   HudHeaderColor = Color.White;
    private static readonly Color   HudPickedColor = Color.LightGreen;
    private static readonly Color   HudNormalColor = new Color(200, 200, 200);

    // ── HUD state (render-thread only) ────────────────────────────────

    private static FocusScore           s_primaryScore;
    private static FocusAndStalenessSelector  s_picker;
    private static LcdRtBucketPolicy          s_bucketPolicy;
    private static IMirrorPluginSettings      s_settings;
    private static LcdOffscreenResolver      s_offscreenResolver;
    private static int[]                      s_sortedIdx = Array.Empty<int>();

    // ── HUD: configuration ────────────────────────────────────────────

    /// <summary>Wire the HUD's score function, settings, and offscreen
    /// resolver. Called once from <c>Plugin.Compose</c>. Safe to re-call
    /// with new instances (e.g. on plugin reload).</summary>
    public static void ConfigureHud(
        FocusScore           primaryScore,
        FocusAndStalenessSelector  picker,
        LcdRtBucketPolicy          bucketPolicy,
        IMirrorPluginSettings      settings,
        LcdOffscreenResolver      offscreenResolver)
    {
        s_primaryScore      = primaryScore      ?? throw new ArgumentNullException(nameof(primaryScore));
        s_picker            = picker            ?? throw new ArgumentNullException(nameof(picker));
        s_bucketPolicy      = bucketPolicy      ?? throw new ArgumentNullException(nameof(bucketPolicy));
        s_settings          = settings          ?? throw new ArgumentNullException(nameof(settings));
        s_offscreenResolver = offscreenResolver ?? throw new ArgumentNullException(nameof(offscreenResolver));
    }

    // ── HUD: draw ─────────────────────────────────────────────────────

    /// <summary>
    /// Render the top-N units (by primary score) as a debug-text
    /// overlay. Each row shows: picked-marker, score, block id,
    /// member count, mode, CenterFactor / Coverage / LookFactor,
    /// distance. Picked units highlight in green. No-op when
    /// <see cref="ConfigureHud"/> hasn't been called or
    /// <see cref="IMirrorPluginSettings.DebugHud"/> is false.
    /// </summary>
    public static void DrawHud(RenderUnit[] units, int unitCount, bool[] picked, long tickCounter,
                               MatrixD playerWorld, bool isPlayerMoving, bool isPlayerInCockpit)
    {
        if (s_primaryScore == null || s_picker == null || s_bucketPolicy == null || s_settings == null) return;

        // Outlines: in debug mode ALL groups draw (the overlay is the
        // whole point of debug mode). With debug off, only groups
        // whose blocks have the terminal "Show on HUD" toggle on draw.
        bool debug = s_settings.DebugHud;
        DrawPanelOutlines(units, unitCount, picked, filterByShowOnHud: !debug);

        // Everything below this point — text rows, detail block —
        // is debug-only.
        if (!debug) return;

        if (unitCount == 0)
        {
            DrawHudText(HudOrigin, "Panels: none", HudHeaderColor);
            return;
        }

        if (s_sortedIdx.Length < unitCount)
            s_sortedIdx = new int[Math.Max(unitCount, s_sortedIdx.Length * 2)];
        for (int i = 0; i < unitCount; i++) s_sortedIdx[i] = i;

        // Insertion sort by primary score (descending). unitCount is
        // small (< 30 typically), so O(N²) is cheaper than allocating
        // a delegate-based sort.
        for (int i = 1; i < unitCount; i++)
        {
            int key = s_sortedIdx[i];
            double keyScore = s_primaryScore.Compute(in units[key], tickCounter);
            int j = i - 1;
            while (j >= 0 && s_primaryScore.Compute(in units[s_sortedIdx[j]], tickCounter) < keyScore)
            {
                s_sortedIdx[j + 1] = s_sortedIdx[j];
                j--;
            }
            s_sortedIdx[j + 1] = key;
        }

        int rows = Math.Min(HudMaxRows, unitCount);
        var pos = HudOrigin;
        DrawHudText(pos,
            $"Panels — top {rows}/{unitCount} (* = picked, group N = #members)",
            HudHeaderColor);
        pos.Y += HudRowHeightPx;

        // Find the unit truly under the crosshair via ray-vs-plane
        // intersection — independent of picking, so the detail block
        // stays anchored to whatever the player is looking at, even if
        // that group isn't rendered this batch (cycling, MP=1, etc.).
        int lookedAtIdx = CrosshairHit.FindIndex(units, unitCount, playerWorld);

        // Surveillance auto-balance scale — compute once for the batch
        // so every row's displayed f/f+s reflects the same scaling the
        // picker actually used. Pass picked=null so the scale matches
        // slot 0's pick (= the canonical priority view, not what slot 1+
        // would see after exclusions).
        double focusScale = s_picker.ComputeFocusScale(
            units, unitCount, picked: null, tickCounter, isPlayerInCockpit);

        for (int r = 0; r < rows; r++)
        {
            int idx = s_sortedIdx[r];
            ref readonly RenderUnit u = ref units[idx];
            var lead   = u.Group.Members[0].Surface;
            double sc    = s_primaryScore.Compute(in u, tickCounter) * focusScale;
            double scStl = s_picker.ComputeWithStaleness(in u, tickCounter, isPlayerInCockpit, focusScale, isPlayerMoving);
            int mc     = u.Group.Members.Count;
            double dM  = Math.Sqrt(u.DistSq);

            string blockId =
                lead.Block?.BlockDefinition.SubtypeName ?? "?";
            string customName =
                lead.Block is IMyTerminalBlock t && !string.IsNullOrEmpty(t.CustomName)
                    ? "\"" + t.CustomName + "\""
                    : "";

            // Show the exact values PanelGroupBuilder compares with:
            //   n=(A,B,C)  Math.Round(Normal.{X,Y,Z} * 1024)  (merge equality)
            //   sd=D       Vector3D.Dot(Origin, Normal)        (merge ±3 cm tolerance)
            //   grid=G     g.GridEntityId                      (merge equality)
            // Two panels that should merge must agree on ALL THREE plus
            // basis. Different gridId is the most common silent
            // rejection — sub-grids on rotors/pistons look adjacent
            // but are separate grid entities.
            var n = u.Group.Normal;
            int nx = (int)Math.Round(n.X * 1024.0);
            int ny = (int)Math.Round(n.Y * 1024.0);
            int nz = (int)Math.Round(n.Z * 1024.0);
            double sd = u.Group.Normal.LengthSquared() > 0.5
                ? Vector3D.Dot(u.Group.Origin, u.Group.Normal)
                : 0;

            // f = focus score (FocusScore Coverage × LookFactor²⁰ /
            //     max(1, DistSq)) scaled by the batch's focus-scale
            //     (1/√N where N = competing focused panels). f+s adds
            //     staleness × StalenessWeight + the mirror priority
            //     bonus. Diff between the two is exactly the staleness
            //     boost the panel currently has — useful for seeing
            //     how long a panel has been waiting for its turn.
            // vp = the scene-render viewport this unit would get if
            //      picked this batch. Mirrors PanelBatchOrchestrator.
            //      RenderUnit exactly: when DistanceResolutionScale is
            //      OFF, render uses the main view resolution; when ON,
            //      LcdRtBucketPolicy picks a bucket from Coverage +
            //      LookFactor + LCD RT size (capped at LCD RT and main
            //      view). HUD must reflect that gate or it reports
            //      the bucket even when LOD is disabled.
            // far = far-clip plane (m) for slot0/slot1+.
            var mainRes = MyRender11.ResolutionI;
            Vector2I vp = mainRes;
            if (s_settings.DistanceResolutionScale
                && s_offscreenResolver != null
                && lead.Block != null
                && s_offscreenResolver.TryResolve(lead.Block, lead.SurfaceIdx, out var lcdInfo)
                && lcdInfo.Rtv != null)
            {
                vp = s_bucketPolicy.ResolutionFor(lcdInfo.Rtv.Size, u.Coverage, u.LookFactor, mainRes);
            }
            float far0 = s_settings.PanelFarClipM;
            float far1 = far0 * 0.5f;
            string line = string.Format(
                "{0} f={1,7:F4} f+s={2,7:F4} vp={16}x{17} far={18:F0}/{19:F0} {3} {4} group={5} grid={6:X} mode={7} n=({8},{9},{10}) sd={11:F3} cf={12:F2} cov={13:F3} lf={14:F2} d={15:F1}m",
                picked[idx] ? "*" : " ",
                sc, scStl, blockId, customName, mc, u.Group.GridEntityId, lead.Mode,
                nx, ny, nz, sd,
                u.CenterFactor, u.Coverage, u.LookFactor, dM,
                vp.X, vp.Y, far0, far1);

            DrawHudText(pos, line,
                picked[idx] ? HudPickedColor : HudNormalColor);
            pos.Y += HudRowHeightPx;
        }

        // Detail block for whatever's geometrically under the crosshair
        // — not the picked unit. Shows the group's plane-coord union
        // AABB and the RT-budget math, so a "this group is too wide,
        // shouldn't have merged" suspicion can be checked against the
        // actual numbers the builder computed.
        if (lookedAtIdx >= 0)
        {
            pos.Y += HudRowHeightPx * 0.5f;        // breathing room
            DrawLookedAtDetail(in units[lookedAtIdx], pos);
        }
    }

    /// <summary>
    /// Multi-line breakdown for the currently-picked unit: which
    /// group it's in, the union AABB extents and dimensions, the
    /// smallest member width/height (drives the RT-budget divisor),
    /// the computed tentative RT size, and whether it would fit the
    /// current main-view RT cap. Diagnostic surface for "why was
    /// this group allowed / rejected by the merge".
    /// </summary>
    private static void DrawLookedAtDetail(in RenderUnit u, Vector2 origin)
    {
        var g    = u.Group;
        var lead = g.Members[0].Surface;

        string subtype = lead.Block?.BlockDefinition.SubtypeName ?? "?";
        string custom  = lead.Block is IMyTerminalBlock t && !string.IsNullOrEmpty(t.CustomName)
            ? "\"" + t.CustomName + "\"" : "";

        double unionW = g.UMax - g.UMin;
        double unionH = g.VMax - g.VMin;

        // Smallest member width / height in the group. The
        // PanelGroupBuilder's FitsRtSizeBudget uses this as the
        // proportional-RT divisor, so a wide union with a small
        // member blows the budget faster than a wide union of equal
        // widths.
        double minW = double.MaxValue, minH = double.MaxValue;
        var members = g.Members;
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            double mw = m.UMax - m.UMin;
            double mh = m.VMax - m.VMin;
            if (mw > 0 && mw < minW) minW = mw;
            if (mh > 0 && mh < minH) minH = mh;
        }
        if (minW == double.MaxValue) minW = unionW;
        if (minH == double.MaxValue) minH = unionH;

        // Match the builder's formula exactly: cap is the engine's
        // main viewport resolution, per axis.
        const int ApproxTargetPx = 512;
        var res = VRageRender.MyRender11.ResolutionI;
        int rtMaxW = res.X > 0 ? res.X : 1024;
        int rtMaxH = res.Y > 0 ? res.Y : 1024;
        double tentRtW = ApproxTargetPx * unionW / Math.Max(0.001, minW);
        double tentRtH = ApproxTargetPx * unionH / Math.Max(0.001, minH);
        bool fits = tentRtW <= rtMaxW && tentRtH <= rtMaxH;

        Color statusColor = fits ? HudPickedColor : Color.OrangeRed;
        var pos = origin;

        DrawHudText(pos,
            $"LOOKED AT: {subtype} {custom} | group={members.Count} mode={lead.Mode}",
            HudPickedColor);
        pos.Y += HudRowHeightPx;

        DrawHudText(pos,
            $"  union U=[{g.UMin:F2}..{g.UMax:F2}] V=[{g.VMin:F2}..{g.VMax:F2}]  W={unionW:F2}m H={unionH:F2}m",
            HudNormalColor);
        pos.Y += HudRowHeightPx;

        DrawHudText(pos,
            $"  minW={minW:F2}m minH={minH:F2}m  members={members.Count}",
            HudNormalColor);
        pos.Y += HudRowHeightPx;

        DrawHudText(pos,
            $"  tentRT={tentRtW:F0}x{tentRtH:F0}px  rtMax={rtMaxW}x{rtMaxH}px  [{(fits ? "FITS" : "OVER")}]",
            statusColor);
        pos.Y += HudRowHeightPx;

        // Resolution + far-plane override this unit would receive.
        // The vp dims match what PanelBatchOrchestrator.RenderUnit
        // would actually push: main view when LOD off, bucket when LOD
        // on and the LCD offscreen is resolved.
        Vector2I leadLcdSize = new Vector2I(0, 0);
        if (lead.Block != null
            && s_offscreenResolver != null
            && s_offscreenResolver.TryResolve(lead.Block, lead.SurfaceIdx, out var leadInfo)
            && leadInfo.Rtv != null)
        {
            leadLcdSize = leadInfo.Rtv.Size;
        }
        Vector2I vp = (s_settings.DistanceResolutionScale && leadLcdSize.X > 0)
            ? s_bucketPolicy.ResolutionFor(leadLcdSize, u.Coverage, u.LookFactor, res)
            : res;
        float farPlane0 = s_settings.PanelFarClipM;
        float farPlane1 = farPlane0 * 0.5f;
        DrawHudText(pos,
            $"  vp: {vp.X}x{vp.Y} (lcd {leadLcdSize.X}x{leadLcdSize.Y})  far: {farPlane0:F0}/{farPlane1:F0}m  cov={u.Coverage:F4}",
            HudNormalColor);
        pos.Y += HudRowHeightPx;

        // Per-member breakdown: where each panel sits in the group's
        // plane coords. Spots cases like "this group is way wider
        // than I expected because an unexpected panel is in it" or
        // "the member supposedly at the right edge is actually inset".
        for (int i = 0; i < members.Count; i++)
        {
            var m = members[i];
            var s = m.Surface;
            string memSubtype = s.Block?.BlockDefinition.SubtypeName ?? "?";
            string memCustom  = s.Block is IMyTerminalBlock mt && !string.IsNullOrEmpty(mt.CustomName)
                ? "\"" + mt.CustomName + "\"" : "";
            double mw = m.UMax - m.UMin;
            double mh = m.VMax - m.VMin;
            // Actual offscreen RTV size — what the renderer writes
            // into for this LCD. Distinct from IMyTextSurface.TextureSize
            // (the surface's logical sprite resolution) when the engine
            // borrows a differently-sized RT for the screen-area pass.
            string rtSize = "?x?";
            if (s.Block != null
                && s_offscreenResolver != null
                && s_offscreenResolver.TryResolve(s.Block, s.SurfaceIdx, out var off)
                && off.Rtv != null)
            {
                var sz = off.Rtv.Size;
                rtSize = $"{sz.X}x{sz.Y}";
            }

            DrawHudText(pos,
                $"  m{i}: {memSubtype} {memCustom} u=[{m.UMin:F2}..{m.UMax:F2}] v=[{m.VMin:F2}..{m.VMax:F2}] ({mw:F2}x{mh:F2}m) rt={rtSize} rot={m.Rotation}",
                HudNormalColor);
            pos.Y += HudRowHeightPx;
        }
    }

    // ── HUD: 3D outlines ──────────────────────────────────────────────

    private static readonly Color OutlineColor       = Color.White;
    private static readonly Color OutlinePickedColor = Color.LightGreen;

    // Engine debug-line API has no width parameter, so we fake
    // thickness by drawing parallel offsets along the group normal.
    // 1.5 cm total spread reads as a clearly thicker stroke at typical
    // viewing distances without bleeding into the panel surface.
    private const double OutlineHalfThickness = 0.0075;

    /// <summary>
    /// Draw a world-space wireframe rectangle around each in-view
    /// group's union AABB — one rect per group, not per member. The
    /// rect spans every member's (U,V) extent, so a multi-member
    /// group reads as a single frame around the whole merged set
    /// rather than N separate frames around individual panels.
    /// Picked groups render in green; others white. Depth-tested,
    /// thickness faked by drawing three offset-along-normal copies.
    ///
    /// <para>When <paramref name="filterByShowOnHud"/> is true, only
    /// groups with at least one member block whose terminal "Show on
    /// HUD" toggle is set draw — lets the player opt individual
    /// blocks in / out of the overlay outside of debug mode. Debug
    /// mode passes false so every group draws unconditionally.</para>
    /// </summary>
    private static void DrawPanelOutlines(RenderUnit[] units, int unitCount, bool[] picked,
                                          bool filterByShowOnHud)
    {
        for (int i = 0; i < unitCount; i++)
        {
            ref readonly RenderUnit u = ref units[i];
            var g = u.Group;
            // Skip groups whose lead plane never resolved — no basis
            // to draw against. Same gate the merge check uses.
            if (g.Normal.LengthSquared() <= 0.5) continue;
            if (filterByShowOnHud && !AnyMemberShowsOnHud(g)) continue;

            Color color = picked[i] ? OutlinePickedColor : OutlineColor;

            // Union AABB corners → world. The group's Origin is the
            // anchor member's screen center; UMin/UMax/VMin/VMax are
            // its plane-coords AABB that the builder grew as members
            // joined.
            Vector3D c00 = g.Origin + g.UMin * g.BasisU + g.VMin * g.BasisV;
            Vector3D c10 = g.Origin + g.UMax * g.BasisU + g.VMin * g.BasisV;
            Vector3D c11 = g.Origin + g.UMax * g.BasisU + g.VMax * g.BasisV;
            Vector3D c01 = g.Origin + g.UMin * g.BasisU + g.VMax * g.BasisV;

            DrawThickQuad(c00, c10, c11, c01, g.Normal, color);
        }
    }

    /// <summary>True iff at least one member block in the group has
    /// its terminal "Show on HUD" toggle on. The block's terminal
    /// type may be missing (defensive null check) — those count as
    /// "not shown".</summary>
    private static bool AnyMemberShowsOnHud(PanelGroup g)
    {
        var members = g.Members;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].Surface.Block is IMyTerminalBlock t && t.ShowOnHUD) return true;
        }
        return false;
    }

    /// <summary>
    /// Draw a 4-edge quad outline at three offsets along the normal
    /// (-h, 0, +h) so the lines visually stack into a thicker stroke.
    /// SE's <c>DebugDrawLine3D</c> has no width; this is the cheapest
    /// way to get a wider appearance without switching to triangle-
    /// strip rendering.
    /// </summary>
    private static void DrawThickQuad(
        Vector3D c00, Vector3D c10, Vector3D c11, Vector3D c01,
        Vector3D normal, Color color)
    {
        for (int pass = -1; pass <= 1; pass++)
        {
            Vector3D ofs = normal * (pass * OutlineHalfThickness);
            Vector3D a = c00 + ofs;
            Vector3D b = c10 + ofs;
            Vector3D c = c11 + ofs;
            Vector3D d = c01 + ofs;
            MyRenderProxy.DebugDrawLine3D(a, b, color, color, depthRead: true);
            MyRenderProxy.DebugDrawLine3D(b, c, color, color, depthRead: true);
            MyRenderProxy.DebugDrawLine3D(c, d, color, color, depthRead: true);
            MyRenderProxy.DebugDrawLine3D(d, a, color, color, depthRead: true);
        }
    }

}
