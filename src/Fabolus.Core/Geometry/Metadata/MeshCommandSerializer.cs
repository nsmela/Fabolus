using BasicResults;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Fabolus.Core.Geometry.Metadata;

/// <summary>
/// Reads and writes a mesh's command history as JSON, for the 3MF package metadata a save file
/// carries. The shape is an array of envelopes:
///
/// <code>[{"Type":"RotateCommand","Data":{...}}, {"Type":"ConcaveMouldDefinition","Data":{...}}]</code>
///
/// The name rather than the CLR type, resolved through <see cref="MeshCommandRegistry"/>, because
/// save files outlive the code that wrote them and a renamed command still has to load. The
/// payload is serialized against the command's runtime type, so an abstract command such as
/// MouldDefinition writes and returns its actual shape rather than its base.
/// </summary>
public static class MeshCommandSerializer {
    private const string TypeProperty = "Type";
    private const string DataProperty = "Data";

    // IncludeFields on both sides, and case-insensitive reads, so a file written by an earlier
    // build with slightly different casing still loads.
    private static readonly JsonSerializerOptions WriteOptions = new() {
        WriteIndented = false,
        IncludeFields = true,
    };

    private static readonly JsonSerializerOptions ReadOptions = new() {
        PropertyNameCaseInsensitive = true,
        IncludeFields = true,
    };

    public static string Serialize(IReadOnlyList<IMeshCommand> commands) {
        var array = new JsonArray();

        foreach (var command in commands) {
            array.Add(new JsonObject {
                [TypeProperty] = MeshCommandRegistry.GetName(command),
                [DataProperty] = JsonSerializer.SerializeToNode(command, command.GetType(), WriteOptions),
            });
        }

        return array.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Rebuilds the history, failing rather than skipping anything it cannot read. A dropped
    /// command would leave the file's baked geometry disagreeing with the history that claims to
    /// describe it, which is a worse outcome than refusing to open it.
    /// </summary>
    public static Result<IReadOnlyList<IMeshCommand>> Deserialize(string json) {
        JsonNode? root;
        try {
            root = JsonNode.Parse(json);
        } catch (JsonException failure) {
            return MetadataErrors.MalformedHistory(failure.Message);
        }

        if (root is not JsonArray array) {
            return MetadataErrors.MalformedHistory("the command history is not a JSON array");
        }

        var commands = new List<IMeshCommand>(array.Count);

        foreach (var entry in array) {
            if (entry is not JsonObject envelope) {
                return MetadataErrors.MalformedHistory("a command entry is not a JSON object");
            }

            var name = envelope[TypeProperty]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) {
                return MetadataErrors.MalformedHistory("a command entry has no type name");
            }

            var type = MeshCommandRegistry.ResolveType(name);
            if (type.IsFailure) return type.Error;

            var data = envelope[DataProperty];
            if (data is null) {
                return MetadataErrors.MalformedHistory($"command '{name}' has no data");
            }

            object? command;
            try {
                command = data.Deserialize(type.Value, ReadOptions);
            } catch (JsonException failure) {
                return MetadataErrors.MalformedHistory($"command '{name}' could not be read: {failure.Message}");
            }

            if (command is not IMeshCommand typed) {
                return MetadataErrors.MalformedHistory($"command '{name}' did not read back as a mesh command");
            }

            commands.Add(typed);
        }

        return Result<IReadOnlyList<IMeshCommand>>.Success(commands);
    }
}
