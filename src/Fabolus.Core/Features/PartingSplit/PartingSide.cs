using System.Text.Json.Serialization;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// Which side of a parting line a split piece represents, relative to the pull direction
/// used to generate it.
/// </summary>
/// <remarks>
/// Serialized by name, not by index: <see cref="SplitCommand"/> persists into the 3MF save file, so
/// the on-disk value must survive reordering of these members.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingSide
{
    /// <summary>The half in the direction of the pull vector.</summary>
    Positive,

    /// <summary>The half opposite the pull vector.</summary>
    Negative
}
