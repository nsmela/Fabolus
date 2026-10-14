using System.Text.Json.Serialization;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>
/// The user's intent for a parting result. The geometry is the same either way - a single mesh whose
/// two halves are separated by the parting-mesh gap - so this only decides how the mesh is written on
/// export: combined stays one file, separated is split into one file per half. Recorded on the
/// <see cref="CutCommand"/> so it persists in a 3MF save and is available when the file is re-exported.
/// </summary>
/// <remarks>Serialized by name so reordering the members can't remap an old save file's value.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PartingResultMode
{
    /// <summary>Keep both halves joined in a single mesh / single exported file.</summary>
    Combined,

    /// <summary>Split the two halves into separate exported files (one per half).</summary>
    Separated
}
