using VRage.Game.Entity;
using VRageMath;

namespace ClientPlugin;

/// <summary>
/// Provides the freshest render-side world matrix for an entity. The
/// game-thread entity's <c>WorldMatrix</c> is one tick ahead of what's
/// applied to the render-side actor on fast-moving grids — using it
/// for a panel render produces visible jitter between reflection and
/// reality. Reading the actor's render-side matrix instead keeps the
/// reflection locked to visible geometry.
/// </summary>
internal interface IActorMatrixSource
{
    /// <summary>Returns the render-side world matrix from the entity's
    /// first valid <see cref="MyActor"/>, or the entity's
    /// <c>WorldMatrix</c> as fallback when no actor has been bound yet.</summary>
    MatrixD GetFreshestMatrix(MyEntity entity);
}
