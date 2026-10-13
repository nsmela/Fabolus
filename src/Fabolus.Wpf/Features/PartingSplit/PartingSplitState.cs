namespace Fabolus.Wpf.Features.PartingSplit;

/// <summary>
/// Stages of the parting split wizard, in the order the user walks them. The view derives its
/// header, body text and step-specific controls from this, and the scene manager derives which
/// visuals are shown.
/// </summary>
public enum PartingSplitState
{
    /// <summary>
    /// The base mesh shaded by which half each face falls in, with the parting line drawn over it as
    /// editable sections - a handle at every join, which the user may drag, drop or add to.
    /// </summary>
    DirectionSelection,

    /// <summary>The generated parting mesh, inside a see-through mould.</summary>
    PartingMeshPreview,

    /// <summary>The resulting mould halves.</summary>
    SplitResult
}
