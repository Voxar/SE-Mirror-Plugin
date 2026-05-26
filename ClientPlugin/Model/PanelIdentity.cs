using System;
using IMyCubeBlock = VRage.Game.ModAPI.Ingame.IMyCubeBlock;

namespace ClientPlugin;

/// <summary>
/// Immutable identity of a registered surface: the block that owns the
/// LCD, plus the surface index (multi-surface blocks like cockpit
/// dashboards expose several LCDs). Equality is by (Block reference,
/// SurfaceIdx); used as the key in <see cref="SurfaceRegistry"/>.
/// </summary>
internal readonly struct PanelIdentity : IEquatable<PanelIdentity>
{
    public readonly IMyCubeBlock Block;
    public readonly int SurfaceIdx;

    public PanelIdentity(IMyCubeBlock block, int surfaceIdx)
    {
        Block = block;
        SurfaceIdx = surfaceIdx;
    }

    /// <summary>Stable numeric key for dictionary use. Collision-safe
    /// for any realistic count of surfaces per block (&lt; 17).</summary>
    public long Key => (Block?.EntityId ?? 0L) * 17L + SurfaceIdx;

    public bool Equals(PanelIdentity other)
        => ReferenceEquals(Block, other.Block) && SurfaceIdx == other.SurfaceIdx;

    public override bool Equals(object obj)
        => obj is PanelIdentity p && Equals(p);

    public override int GetHashCode()
        => unchecked(((Block?.EntityId.GetHashCode()) ?? 0) * 17 + SurfaceIdx);

    public override string ToString()
        => $"PanelIdentity(block={Block?.EntityId ?? 0}, surf={SurfaceIdx})";
}
