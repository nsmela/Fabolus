using BasicResults;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Defines standard error results related to metadata operations.
/// </summary>
public static class MetadataErrors {
    /// <summary>
    /// Error returned when a requested metadata key is not present in the dictionary.
    /// </summary>
    public static readonly Error KeyNotFound = new("Metadata.NotFound", "The key used is not found in the metadata");

    /// <summary>
    /// Error returned when an operation needs the recorded base mesh but none exists.
    /// </summary>
    public static readonly Error MissingBaseMesh = new("Metadata.MissingBaseMesh", "The mesh has no recorded base mesh to replay from");

    /// <summary>
    /// Error returned when a save file records a command this build cannot resolve to a type.
    /// </summary>
    public static Error UnknownCommand(string name) =>
        new("Metadata.UnknownCommand", $"Unrecognised mesh command '{name}'. The file was saved by a different version of Fabolus; add the name to MeshCommandRegistry if the command was renamed.");

    /// <summary>
    /// Error returned when a save file's recorded history cannot be read at all. Distinct from
    /// <see cref="UnknownCommand"/>: that one names a command this build does not know, where
    /// this one means the history itself is damaged.
    /// </summary>
    public static Error MalformedHistory(string detail) =>
        new("Metadata.MalformedHistory", $"The saved mesh history could not be read: {detail}.");
}
