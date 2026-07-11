namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Which side of a parting line a split piece represents, relative to the pull direction
/// used to generate it.
/// </summary>
public enum PartingSide
{
    /// <summary>The half in the direction of the pull vector.</summary>
    Positive,

    /// <summary>The half opposite the pull vector.</summary>
    Negative
}
