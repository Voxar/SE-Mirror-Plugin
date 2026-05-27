using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Walks an LCD block's mesh to derive the screen-surface plane (center,
/// outward normal, in-plane right/up basis, half extents) in MESH-LOCAL
/// coordinates. The plane is constant for a given (block definition,
/// screen material) pair across all instances of that LCD type, so
/// resolved results — including negative results — are cached process-
/// wide.
///
/// Algorithm (ported from the original MirrorRenderer):
/// <list type="number">
///   <item>Find the submesh whose material matches the screen
///         material name. Bail if absent.</item>
///   <item>Pass 1: compute the outward normal as the area-weighted
///         sum of triangle normals. If that sum cancels to near zero
///         the mesh is double-sided (transparent LCD); re-sum with
///         hemisphere alignment against the largest triangle.</item>
///   <item>Compute the in-plane up direction from UV gradients (V=0
///         at the top per DirectX convention). Falls back to block-
///         local Y projected onto the plane if UV data is missing or
///         degenerate, then to Z if Y is too close to the normal.</item>
///   <item>Right = Up × Normal (RH viewer basis).</item>
///   <item>Pass 2: bounding box every vertex in the (Right, Up,
///         Normal) frame. Center is the midpoint along Right/Up;
///         along Normal we pin the center to the outermost face so
///         the LCD plane sits on the visible surface, not inside
///         the mesh.</item>
/// </list>
/// </summary>
internal sealed class ScreenPlaneResolver
{
    // Process-wide cache of SUCCESSFUL resolves only. The plane
    // geometry for a (BlockDef, material) is deterministic — once
    // we've computed it once, it never changes — so caching
    // successes forever is safe. Failures (model / render-component
    // / screen-areas in a transient state at the moment of the
    // call) are NOT cached: every call retries them. Mirror panels
    // registered mid-transient stay flagged unresolved upstream
    // (PanelGroupBuilder._hasUnresolvedMirrorPlanes), forcing a
    // fresh Rebuild each batch until the resolver succeeds.
    private readonly Dictionary<MyCubeBlockDefinition, Dictionary<string, ScreenPlaneInfo>> _cache
        = new();
    private readonly object _lock = new();

    public bool TryResolve(MyEntity blockEntity, string materialName, out ScreenPlaneInfo info)
    {
        info = default;
        if (blockEntity == null || string.IsNullOrEmpty(materialName)) return false;
        if (!(blockEntity is MyCubeBlock cubeBlock)) return false;
        var def = cubeBlock.BlockDefinition;
        if (def == null) return false;

        lock (_lock)
        {
            if (!_cache.TryGetValue(def, out var perMaterial))
            {
                perMaterial = new Dictionary<string, ScreenPlaneInfo>(StringComparer.OrdinalIgnoreCase);
                _cache[def] = perMaterial;
            }
            if (perMaterial.TryGetValue(materialName, out var cachedSuccess))
            {
                info = cachedSuccess;
                return true;
            }

            ScreenPlaneInfo? computed = ComputeLocal(blockEntity, def, materialName);
            if (!computed.HasValue) return false;     // transient — try again next call
            perMaterial[materialName] = computed.Value;
            info = computed.Value;
            return true;
        }
    }

    // ── Implementation ──────────────────────────────────────────────

    private static ScreenPlaneInfo? ComputeLocal(
        MyEntity blockEntity, MyCubeBlockDefinition def, string materialName)
    {
        try
        {
            var rc = blockEntity.Render;
            if (rc == null) return null;
            var model = rc.ModelStorage as MyModel;
            if (model == null) return null;

            var meshes = model.GetMeshList();
            if (meshes == null) return null;

            VRageRender.Models.MyMesh screenMesh = null;
            for (int i = 0; i < meshes.Count; i++)
            {
                var mesh = meshes[i];
                if (mesh == null || mesh.Material == null) continue;
                if (string.Equals(mesh.Material.Name, materialName, StringComparison.OrdinalIgnoreCase))
                {
                    screenMesh = mesh;
                    break;
                }
            }
            if (screenMesh == null) return null;

            int triStart = screenMesh.TriStart;
            int triCount = screenMesh.TriCount;
            if (triCount <= 0) return null;

            // Pass 1: area-weighted normal via naive triangle-normal sum.
            // For CCW-wound single-sided meshes this points outward;
            // for CW-wound meshes it points inward. Disambiguated below
            // using the model's screen *dummy* — a modeler-placed
            // marker whose orientation matrix encodes the screen's
            // intended outward direction (the dummy's Forward axis
            // looks out of the screen toward the viewer). This is the
            // same kind of marker SE uses for gun muzzles, exhaust
            // outlets, and cockpit head positions.
            //
            // If the naive sum cancels to near zero, the mesh is
            // double-sided (back-to-back triangles, TransparentScreenArea);
            // re-sum with hemisphere alignment against the largest
            // triangle. For double-sided meshes "outward" is arbitrary
            // — both sides are valid viewing surfaces — so the dummy
            // sign-check doesn't apply.
            Vector3 naiveSum = Vector3.Zero;
            Vector3 largestTriNormal = Vector3.Zero;
            float largestTriArea2 = 0f;
            float totalArea = 0f;
            for (int t = triStart; t < triStart + triCount; t++)
            {
                var tri = model.GetTriangle(t);
                var v0 = model.GetVertex(tri.I0);
                var v1 = model.GetVertex(tri.I1);
                var v2 = model.GetVertex(tri.I2);
                Vector3 nTri = Vector3.Cross(v1 - v0, v2 - v0);
                float area2 = nTri.LengthSquared();
                if (area2 < 1e-12f) continue;
                naiveSum += nTri;
                totalArea += (float)Math.Sqrt(area2);
                if (area2 > largestTriArea2)
                {
                    largestTriArea2 = area2;
                    largestTriNormal = nTri;
                }
            }

            // Look up the matching dummy (in SE convention each
            // ScreenArea.Name is BOTH the material name AND a dummy
            // name). Forward axis of the dummy points outward through
            // the screen face.
            bool hasDummyOutward = false;
            Vector3 dummyOutward = Vector3.Zero;
            if (model.Dummies != null
                && model.Dummies.TryGetValue(materialName, out var screenDummy))
            {
                dummyOutward = screenDummy.Matrix.Forward;
                if (dummyOutward.LengthSquared() > 1e-6f)
                {
                    dummyOutward.Normalize();
                    hasDummyOutward = true;
                }
            }

            Vector3 localNormal;
            bool doubleSided;
            if (naiveSum.LengthSquared() > totalArea * totalArea * 0.04f)
            {
                localNormal = Vector3.Normalize(naiveSum);
                doubleSided = false;

                // Sign correction against the dummy's outward direction.
                // If the mesh-derived normal disagrees with the dummy
                // (dot < 0), it's CW-wound — flip. If no dummy is
                // available, fall back to the CCW assumption.
                if (hasDummyOutward
                    && Vector3.Dot(localNormal, dummyOutward) < 0f)
                {
                    localNormal = -localNormal;
                }
            }
            else
            {
                doubleSided = true;
                if (largestTriNormal == Vector3.Zero) return null;
                Vector3 aligned = Vector3.Zero;
                for (int t = triStart; t < triStart + triCount; t++)
                {
                    var tri = model.GetTriangle(t);
                    var v0 = model.GetVertex(tri.I0);
                    var v1 = model.GetVertex(tri.I1);
                    var v2 = model.GetVertex(tri.I2);
                    Vector3 nTri = Vector3.Cross(v1 - v0, v2 - v0);
                    if (nTri.LengthSquared() < 1e-12f) continue;
                    if (Vector3.Dot(nTri, largestTriNormal) < 0f) nTri = -nTri;
                    aligned += nTri;
                }
                if (aligned.LengthSquared() < 1e-12f) return null;
                localNormal = Vector3.Normalize(aligned);
            }

            // Screen-up direction: UV-based first (works for any
            // orientation); fall back to block-local Y projected onto
            // the plane, then Z if Y is too close to the normal.
            Vector3 localUp = ComputeUpFromUVs(model, triStart, triCount, localNormal);
            if (localUp.LengthSquared() < 1e-6f)
            {
                Vector3 candidate = Vector3.UnitY;
                if (Math.Abs(Vector3.Dot(localNormal, candidate)) > 0.95f)
                    candidate = Vector3.UnitZ;
                localUp = candidate - Vector3.Dot(candidate, localNormal) * localNormal;
                if (localUp.LengthSquared() < 1e-6f) return null;
            }
            localUp = Vector3.Normalize(localUp);

            // Right = Up × Normal (right-handed viewer basis).
            Vector3 localRight = Vector3.Normalize(Vector3.Cross(localUp, localNormal));

            // Pass 2: bounding box in (Right, Up, Normal).
            float minR = float.PositiveInfinity, maxR = float.NegativeInfinity;
            float minU = float.PositiveInfinity, maxU = float.NegativeInfinity;
            float minN = float.PositiveInfinity, maxN = float.NegativeInfinity;
            for (int t = triStart; t < triStart + triCount; t++)
            {
                var tri = model.GetTriangle(t);
                int i0 = tri.I0, i1 = tri.I1, i2 = tri.I2;
                AccumulateExtents(model.GetVertex(i0), localRight, localUp, localNormal,
                                  ref minR, ref maxR, ref minU, ref maxU, ref minN, ref maxN);
                AccumulateExtents(model.GetVertex(i1), localRight, localUp, localNormal,
                                  ref minR, ref maxR, ref minU, ref maxU, ref minN, ref maxN);
                AccumulateExtents(model.GetVertex(i2), localRight, localUp, localNormal,
                                  ref minR, ref maxR, ref minU, ref maxU, ref minN, ref maxN);
            }

            float centerR = (minR + maxR) * 0.5f;
            float centerU = (minU + maxU) * 0.5f;
            // Pin center to the outermost face along the normal so the
            // LCD plane sits on the actual visible face (not the centroid
            // of mesh thickness).
            float anchorN = maxN;

            Vector3 localCenter =
                  centerR * localRight
                + centerU * localUp
                + anchorN * localNormal;
            float halfWidth  = (maxR - minR) * 0.5f;
            float halfHeight = (maxU - minU) * 0.5f;

            return new ScreenPlaneInfo(
                localCenter, localNormal, localRight, localUp,
                halfWidth, halfHeight, doubleSided);
        }
        catch
        {
            return null;
        }
    }

    private static void AccumulateExtents(
        Vector3 v, Vector3 right, Vector3 up, Vector3 normal,
        ref float minR, ref float maxR,
        ref float minU, ref float maxU,
        ref float minN, ref float maxN)
    {
        float r = Vector3.Dot(v, right);
        float u = Vector3.Dot(v, up);
        float n = Vector3.Dot(v, normal);
        if (r < minR) minR = r; if (r > maxR) maxR = r;
        if (u < minU) minU = u; if (u > maxU) maxU = u;
        if (n < minN) minN = n; if (n > maxN) maxN = n;
    }

    /// <summary>
    /// World-space direction in which UV.V decreases. By DirectX
    /// convention V=0 is the top of the texture, so dV/dpos points
    /// DOWN — we negate to get screen-up. Returns Vector3.Zero if UV
    /// data is missing or every triangle is degenerate in UV space.
    /// </summary>
    private static Vector3 ComputeUpFromUVs(MyModel model, int triStart, int triCount, Vector3 localNormal)
    {
        try
        {
            // TexCoords is lazy-loaded — touch LoadTexCoordData first.
            try { model.LoadTexCoordData(); } catch { }
            var uvArray = model.TexCoords;
            if (uvArray == null || uvArray.Length == 0) return Vector3.Zero;

            Vector3 accumulated = Vector3.Zero;
            for (int t = triStart; t < triStart + triCount; t++)
            {
                var tri = model.GetTriangle(t);
                if (tri.I0 >= uvArray.Length || tri.I1 >= uvArray.Length || tri.I2 >= uvArray.Length) continue;

                var v0 = model.GetVertex(tri.I0);
                var v1 = model.GetVertex(tri.I1);
                var v2 = model.GetVertex(tri.I2);

                Vector2 uv0 = uvArray[tri.I0].ToVector2();
                Vector2 uv1 = uvArray[tri.I1].ToVector2();
                Vector2 uv2 = uvArray[tri.I2].ToVector2();

                Vector3 e1 = v1 - v0;
                Vector3 e2 = v2 - v0;
                Vector2 du1 = uv1 - uv0;
                Vector2 du2 = uv2 - uv0;

                float D = du1.X * du2.Y - du1.Y * du2.X;
                if (Math.Abs(D) < 1e-10f) continue;

                // dPos/dV in world coords:  dPos/dV = (-du2.U * e1 + du1.U * e2) / D
                Vector3 dPdV = (-du2.X * e1 + du1.X * e2) / D;
                // Strip out-of-plane component so mesh thickness doesn't tilt the result.
                dPdV -= Vector3.Dot(dPdV, localNormal) * localNormal;

                float tri3DArea = 0.5f * (float)Math.Sqrt(Vector3.Cross(e1, e2).LengthSquared());
                if (tri3DArea < 1e-10f) continue;
                accumulated += dPdV * tri3DArea;
            }

            // V=0 at top → screen-up = -dPos/dV.
            accumulated = -accumulated;
            if (accumulated.LengthSquared() < 1e-10f) return Vector3.Zero;
            accumulated -= Vector3.Dot(accumulated, localNormal) * localNormal;
            if (accumulated.LengthSquared() < 1e-10f) return Vector3.Zero;
            return accumulated;
        }
        catch
        {
            return Vector3.Zero;
        }
    }
}
