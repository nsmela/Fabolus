using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// One point on a flange's inner rim, with how far inside the body it sits.
///
/// <para>
/// The rim is what seals the parting mesh against the mould cavity, and it is placed by footprint
/// arithmetic - offset inward from the parting line in plan, then given the height of the nearest
/// parting point. Where the parting line's height changes quickly that arithmetic puts a vertex
/// slightly outside the body, and every such vertex is a hairline bridge of mould material that
/// survives the cut, leaving the mould in one piece however thick the cutter is. On scalp.3mf four
/// vertices out of 248 were enough to make the split fail at every thickness up to 1mm.
/// </para>
///
/// <para>
/// It is worth surfacing because the failure is otherwise invisible until the boolean runs and
/// reports nothing more useful than "did not separate into two halves". Shown on the parting-mesh
/// preview, a single point on the wrong side of the surface says which stretch of the rim is at
/// fault and that the split is going to fail, before the user commits to it.
/// </para>
/// </summary>
/// <param name="Position">The rim vertex, in world space.</param>
/// <param name="SignedDistance">
/// Distance to the body surface: negative inside, positive outside. Inside is what seals.
/// </param>
public readonly record struct FlangeSealPoint(Vector3 Position, float SignedDistance)
{
    /// <summary>True when the point is inside the body, i.e. it seals rather than bridges.</summary>
    public bool IsSealed => SignedDistance < 0f;
}
