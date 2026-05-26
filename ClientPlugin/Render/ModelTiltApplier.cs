using System;
using System.Collections.Generic;
using Sandbox.Game.Components;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRageMath;
using VRageRender;

namespace ClientPlugin;

/// <summary>
/// Renders a visual hint of the per-LCD mirror yaw/pitch on the LCD's
/// mesh: each sim tick, pushes a tilted local matrix to the engine's
/// render thread for every eligible mirror panel, so the screen's
/// mesh visibly rotates by the same amount as the slider.
///
/// <para>Render-only: the entity's <see cref="MyEntity.PositionComp"/>
/// is never mutated, so collision, AABB, ownership, attachment, and
/// the terminal control wiring all see the block at its original
/// orientation. The render thread sees the tilted child-to-parent
/// matrix instead.</para>
///
/// <para><b>Pivot strategy.</b> The rotation pivots around the corner
/// of the screen in the LEAN direction (the corner offset by
/// <c>+sign(degX)·HalfWidth</c> along <c>LocalRight</c> and
/// <c>+sign(degY)·HalfHeight</c> along <c>LocalUp</c>). That edge stays
/// pinned to its original position and the OPPOSITE corner swings
/// backward into the block — the panel "opens like a door" inward,
/// guaranteeing the mesh never extends past its original cube footprint
/// regardless of how the two angles combine.</para>
///
/// <para><b>Eligibility.</b> Only panels that pass both of:
/// <list type="bullet">
///   <item>screen normal is axis-aligned in block-local frame (within
///         ~1° tolerance) — rejects sloped / corner / arbitrary
///         orientations whose pivot wouldn't stay flush with a face;</item>
///   <item>screen sits in the back half of the block along the normal
///         (= more than 50% inset) — rejects flush / protruding panels
///         where tilt would poke through the front face.</item>
/// </list>
/// Eligibility is determined once per panel from its cached
/// <see cref="ScreenPlaneInfo"/> (mesh-local, immutable for a given
/// block definition × screen material pair).</para>
/// </summary>
internal sealed class ModelTiltApplier
{
    private readonly ISurfaceRegistry      _registry;
    private readonly IScreenPlaneResolver  _planeResolver;

    private readonly Dictionary<long, State> _state = new();

    /// <summary>
    /// Per-panel tracking. The plane info is cached after the first
    /// successful resolve because it depends only on (block definition,
    /// screen material) and never changes across the panel's lifetime.
    /// Eligibility is cached for the same reason.
    ///
    /// <para><see cref="EverPushedNonZero"/> tracks whether we've ever
    /// pushed a non-identity tilt for this panel — so when the user
    /// dials the angles back to (0, 0) we know to push the IDENTITY
    /// tilt once to undo the previous push. Without it, SE's render
    /// matrix would stay stuck at the last-pushed tilt forever (the
    /// storage updates correctly but the visible mesh and the
    /// reflection plane both follow the actor matrix, which only the
    /// applier resets).</para>
    /// </summary>
    private struct State
    {
        public ScreenPlaneInfo Local;
        public bool            LocalResolved;
        public bool            Eligible;
        public bool            EverPushedNonZero;
    }

    public ModelTiltApplier(
        ISurfaceRegistry      registry,
        IScreenPlaneResolver  planeResolver)
    {
        _registry      = registry;
        _planeResolver = planeResolver;
    }

    public void OnSimTick()
    {
        var surfaces = _registry.SnapshotForRender();
        if (surfaces == null) return;

        for (int i = 0; i < surfaces.Length; i++)
        {
            var s = surfaces[i];
            if (s == null) continue;
            if (s.Mode != PanelMode.Mirror) continue;

            float degX = MirrorAngleClamp.Clamp(s.Config.MirrorAngleDegX, MirrorAngleClamp.HardMaxDeg);
            float degY = MirrorAngleClamp.Clamp(s.Config.MirrorAngleDegY, MirrorAngleClamp.HardMaxDeg);

            long key = s.Key;
            if (!_state.TryGetValue(key, out var st)) st = default;

            // Skip the push when the angles are zero AND we've never
            // pushed a non-zero tilt for this panel — there's nothing
            // to set or to undo. If we HAVE pushed a non-zero tilt
            // before, we must push the identity tilt now to reset
            // SE's render matrix back to baseLocal; otherwise the
            // visible mesh AND the reflection plane (which derives
            // from the actor matrix) stay stuck at the last-applied
            // tilt forever.
            if (degX == 0f && degY == 0f && !st.EverPushedNonZero) continue;

            Apply(s, degX, degY, ref st);
            _state[key] = st;
        }
    }

    private void Apply(PanelSurface surface, float degX, float degY, ref State st)
    {
        var blockEntity = surface.Block as MyEntity;
        if (blockEntity == null) return;

        // Resolve the local plane once, then evaluate eligibility once.
        // Both are immutable for the panel's lifetime.
        if (!st.LocalResolved)
        {
            if (!TryResolveLocalPlane(blockEntity, surface.SurfaceIdx, out st.Local))
                return;
            st.LocalResolved = true;
            st.Eligible      = IsEligibleForMeshTilt(blockEntity, in st.Local);
        }

        if (!st.Eligible)
        {
            // Don't push — leave the engine's standard local-matrix
            // linkage untouched.
            return;
        }

        var render = blockEntity.Render;
        if (render == null) return;
        var roIds = render.RenderObjectIDs;
        if (roIds == null || roIds.Length == 0) return;

        var cubeBlock = blockEntity as MyCubeBlock;
        if (cubeBlock == null || cubeBlock.CubeGrid == null) return;
        var cell = cubeBlock.CubeGrid.RenderData?.GetOrAddCell(
            cubeBlock.Position * cubeBlock.CubeGrid.GridSize);
        if (cell == null) return;
        uint parentId = cell.ParentCullObject;
        if (parentId == uint.MaxValue) return;

        // Tilt math is in BLOCK-LOCAL frame. Sign(deg) picks which
        // edge becomes the pivot — the one the lean is going toward.
        // Sign(0) is 0 which would put the pivot at the screen centre
        // along that axis; that's harmless because the rotation
        // amount is also 0 around that axis.
        const double Deg2Rad = Math.PI / 180.0;
        double yawRad   = degX * Deg2Rad;
        double pitchRad = degY * Deg2Rad;

        Vector3D upAxis    = (Vector3D)st.Local.LocalUp;
        Vector3D rightAxis = (Vector3D)st.Local.LocalRight;

        Vector3D pivot = (Vector3D)st.Local.LocalCenter
                       + Math.Sign(degX) * st.Local.HalfWidth  * rightAxis
                       + Math.Sign(degY) * st.Local.HalfHeight * upAxis;

        // Rotation around (axis-through-pivot): translate pivot to
        // origin, rotate, translate back. Yaw before pitch — order
        // matters only in that the second rotation's axis stays the
        // ORIGINAL LocalRight (not the post-yaw rotated right). For
        // small/moderate angles the difference is invisible.
        //
        // Sign convention: the opposite edge swings OUT toward the
        // viewer for both axes — positive yaw → +Right edge pinned,
        // left swings forward; positive pitch → +Up edge pinned,
        // bottom swings forward. Row-vector RH rotation matrices
        // around +Up vs +Right differ in handedness of "outward",
        // hence the negation on pitch (without it, yaw would swing
        // outward but pitch would swing into the block — what the
        // user saw before).
        MatrixD rYaw   = MatrixD.CreateFromAxisAngle(upAxis,     yawRad);
        MatrixD rPitch = MatrixD.CreateFromAxisAngle(rightAxis, -pitchRad);
        MatrixD tilt   = MatrixD.CreateTranslation(-pivot)
                       * rYaw * rPitch
                       * MatrixD.CreateTranslation(pivot);

        // Row-vector convention: applying tilt as left-multiply means
        // the mesh is tilted in its OWN local frame before the
        // existing local-to-parent transform. Translation of the
        // block's grid position is preserved by tilt's identity
        // translation column.
        var baseLocal   = blockEntity.PositionComp.LocalMatrixRef;
        var tiltedLocal = tilt * baseLocal;

        // One push per render object (cube blocks with custom render
        // components — e.g. MyRenderComponentScreenAreas — can own
        // more than one). SetParentCullObject re-publishes the
        // child-to-parent matrix without changing the parent linkage.
        var childToParent = (Matrix)tiltedLocal;
        for (int i = 0; i < roIds.Length; i++)
        {
            uint roId = roIds[i];
            if (roId == uint.MaxValue) continue;
            MyRenderProxy.SetParentCullObject(roId, parentId, childToParent);
        }

        // Latch so a future transition back to (0, 0) still triggers
        // one more push — the identity-tilt one that resets the
        // render matrix.
        if (degX != 0f || degY != 0f) st.EverPushedNonZero = true;
    }

    /// <summary>
    /// Two rules + a whitelist:
    /// <list type="number">
    ///   <item>screen normal is axis-aligned to one of the block's six
    ///         face directions (within ~2.5° tolerance) — rejects sloped /
    ///         corner / curved-apex LCDs where the pivot edge wouldn't
    ///         stay flush with a face;</item>
    ///   <item>block depth along that normal is less than
    ///         <see cref="ThinDepthFractionOfGrid"/> of the grid's cube
    ///         size — rejects full-cube LCDs (Corner LCD Panel, Inset LCD Panel,
    ///         etc.) where a tilted mesh would intersect other blocks; only slim panels
    ///         (Wide LCD, Transparent LCD, ...) qualify;</item>
    ///   <item>plus <see cref="AlwaysEligibleSubtypes"/> — specific
    ///         subtypes whose screen is angled but geometrically small
    ///         enough that tilting it doesn't poke past the cube
    ///         footprint, despite failing the axis-aligned rule.</item>
    /// </list>
    /// </summary>
    private const float ThinDepthFractionOfGrid = 0.4f;

    // Vanilla corner LCDs ship in two model variants per size — the
    // numbered ones (_1 = Top, _2 = Bottom) have the screen on the
    // angled 45° face of the corner block, the _Flat_ variants have
    // it on a normal axis-aligned face (those already pass the
    // axis-aligned rule, no whitelist needed for them).
    private static readonly HashSet<string> AlwaysEligibleSubtypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "LargeBlockCorner_LCD_1",   // Corner LCD Top, large grid
        "LargeBlockCorner_LCD_2",   // Corner LCD Bottom, large grid
        "SmallBlockCorner_LCD_1",   // Corner LCD Top, small grid
        "SmallBlockCorner_LCD_2",   // Corner LCD Bottom, small grid
    };

    private static bool IsEligibleForMeshTilt(MyEntity blockEntity, in ScreenPlaneInfo local)
    {
        var cubeBlock = blockEntity as MyCubeBlock;
        if (cubeBlock == null || cubeBlock.CubeGrid == null) return false;

        // Whitelist shortcut: angled-but-small subtypes the general
        // rules would otherwise reject.
        var def = cubeBlock.BlockDefinition;
        if (def != null && AlwaysEligibleSubtypes.Contains(def.Id.SubtypeName))
            return true;

        Vector3 n = local.LocalNormal;
        float ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
        const float AxisAlignedThreshold = 0.999f;
        bool axisAligned = ax > AxisAlignedThreshold
                        || ay > AxisAlignedThreshold
                        || az > AxisAlignedThreshold;
        if (!axisAligned) return false;

        // The block's local AABB extent on the axis the normal points
        // along. Axis-aligned normal means we can pick the per-axis
        // extent directly without projecting all 8 corners.
        var aabb = blockEntity.PositionComp.LocalAABB;
        float depth = ax > AxisAlignedThreshold ? aabb.Max.X - aabb.Min.X
                    : ay > AxisAlignedThreshold ? aabb.Max.Y - aabb.Min.Y
                    :                             aabb.Max.Z - aabb.Min.Z;

        return depth < cubeBlock.CubeGrid.GridSize * ThinDepthFractionOfGrid;
    }

    /// <summary>
    /// Find the first screen material on <paramref name="blockEntity"/>'s
    /// surface index, then resolve the screen plane in block-local
    /// coordinates. Returns false when the block's render component
    /// isn't a screen-areas render or hasn't initialised yet — caller
    /// retries next sim tick.
    /// </summary>
    private bool TryResolveLocalPlane(MyEntity blockEntity, int surfaceIdx, out ScreenPlaneInfo local)
    {
        local = default;
        var screenRC = blockEntity.Render as MyRenderComponentScreenAreas;
        if (screenRC == null) return false;
        var areas = screenRC.m_screenAreas;
        if (areas == null || areas.Count == 0) return false;
        int idx = (surfaceIdx >= 0 && surfaceIdx < areas.Count) ? surfaceIdx : 0;
        string material = areas[idx].Material;
        if (string.IsNullOrEmpty(material)) return false;
        return _planeResolver.TryResolve(blockEntity, material, out local);
    }
}
