using System.Collections.Immutable;
using System.Text.Json;
using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry.Metadata;
using GE = GeometryEngine.Core.Geometry;
using GEC = GeometryEngine.Core.Common;

namespace Fabolus.Core.Geometry.Engine;

/// <summary>
/// Mesh files through the library's readers and writers, with every byte going through
/// <see cref="IFileSystem"/>. The one thing that is Fabolus's own is a 3MF's contents: the
/// command history as vendor metadata, and the base mesh as the package's reference object -
/// the same layout MeshLib-era Fabolus wrote, so old files still open.
/// </summary>
internal sealed class EngineIO(IFileSystem fileSystem, GE.IGeometryEngine engine, IGeometryEvaluators evaluators) : IGeometryIO
{
    private const string CommandsKey = "fab:Commands";

    private static readonly GE.PackageVendor Vendor = new("fab", "http://fabolus.io/2026/metadata", "basemesh");

    public Result<IMesh> Import(string filePath)
    {
        if (!fileSystem.Exists(filePath)) return EngineErrors.FileNotFound(filePath);

        var format = GE.MeshFileFormats.FromPath(filePath);
        if (format.HasNoValue) return EngineErrors.UnsupportedFormat(Path.GetExtension(filePath));

        byte[] bytes;
        try
        {
            bytes = fileSystem.ReadAllBytes(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return EngineErrors.ReadFailed(ex.Message);
        }

        var metadata = MeshMetadata.FromFileName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);

        GE.IMesh model;
        if (format.Value == GE.MeshFileFormat.ThreeMf)
        {
            var package = engine.IO.ReadPackage(bytes, name, Vendor);
            if (package.IsFailure) return EngineErrors.ReadFailed(package.Error.Description);

            var withCommands = ReadCommands(package.Value.Metadata, metadata);
            if (withCommands.IsFailure) return withCommands.Error;
            metadata = withCommands.Value;

            if (package.Value.Reference.HasValue)
            {
                metadata = metadata.WithBaseMesh(package.Value.Reference.Value.ToFabolus(MeshMetadata.FromFileName("BaseMesh")));
            }

            model = package.Value.Model;
        }
        else
        {
            var read = engine.IO.Read(bytes, format.Value, name);
            if (read.IsFailure) return EngineErrors.ReadFailed(read.Error.Description);
            model = read.Value;
        }

        IMesh mesh = model.ToFabolus(metadata);

        var validation = evaluators.ValidateTopology(mesh);
        if (validation.IsSuccess)
        {
            mesh = mesh.WithMetadata(metadata.WithTopology(validation.Value));
        }

        return Result.Success(mesh);
    }

    public Result Export(IMesh mesh, string filePath, bool overwrite = false)
    {
        if (fileSystem.Exists(filePath) && !overwrite) return EngineErrors.FileExists(filePath);

        var format = GE.MeshFileFormats.FromPath(filePath);
        if (format.HasNoValue) return EngineErrors.UnsupportedFormat(Path.GetExtension(filePath));

        var converted = mesh.ToEngine();
        if (converted.IsFailure) return converted.Error;

        GEC.Result<byte[]> bytes;
        if (format.Value == GE.MeshFileFormat.ThreeMf)
        {
            var reference = mesh.Metadata.GetBaseMesh();
            var referenceMesh = GEC.Maybe<GE.IMesh>.None();
            if (reference.HasValue)
            {
                var convertedReference = reference.Value.ToEngine();
                if (convertedReference.IsFailure) return convertedReference.Error;
                referenceMesh = GEC.Maybe<GE.IMesh>.Some(convertedReference.Value);
            }

            var metadata = ImmutableDictionary<string, string>.Empty.Add(CommandsKey, WriteCommands(mesh.Metadata));
            bytes = engine.IO.WritePackage(new GE.MeshPackage(converted.Value, referenceMesh, metadata, Vendor));
        }
        else
        {
            bytes = engine.IO.Write(converted.Value, format.Value);
        }

        if (bytes.IsFailure) return EngineErrors.WriteFailed(bytes.Error.Description);

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !fileSystem.DirectoryExists(directory))
            {
                fileSystem.CreateDirectory(directory);
            }

            fileSystem.WriteAllBytes(filePath, bytes.Value);
            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return EngineErrors.AccessDenied(filePath, ex.Message);
        }
        catch (IOException ex)
        {
            return EngineErrors.WriteFailed(ex.Message);
        }
    }

    private static string WriteCommands(MeshMetadata metadata)
    {
        var records = metadata.Commands.Select(c => new
        {
            Type = MeshCommandRegistry.GetName(c),
            Data = (object)c,
        });

        return JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
    }

    /// <summary>
    /// Replays the stored command history onto the metadata. A command that fails to load is a
    /// hard failure, never a skip: the geometry already has it baked in, so dropping it would leave
    /// the history disagreeing with the model, and every replay-from-base view would render
    /// something other than what was imported.
    /// </summary>
    private static Result<MeshMetadata> ReadCommands(ImmutableDictionary<string, string> packageMetadata, MeshMetadata metadata)
    {
        if (!packageMetadata.TryGetValue(CommandsKey, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return Result.Success(metadata);
        }

        JsonElement[]? records;
        try
        {
            records = JsonSerializer.Deserialize<JsonElement[]>(json);
        }
        catch (JsonException ex)
        {
            return EngineErrors.ReadFailed($"Command history is not valid JSON: {ex.Message}");
        }

        foreach (var record in records ?? [])
        {
            if (!record.TryGetProperty("Type", out var typeElement) || !record.TryGetProperty("Data", out var dataElement))
            {
                return EngineErrors.ReadFailed("A command history entry is missing its Type or Data.");
            }

            var type = MeshCommandRegistry.ResolveType(typeElement.GetString() ?? string.Empty);
            if (type.IsFailure) return type.Error;

            IMeshCommand? command;
            try
            {
                command = (IMeshCommand?)JsonSerializer.Deserialize(
                    dataElement.GetRawText(),
                    type.Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
            }
            catch (JsonException ex)
            {
                return EngineErrors.ReadFailed($"Could not read the '{type.Value.Name}' command: {ex.Message}");
            }

            if (command is null)
            {
                return EngineErrors.ReadFailed($"The '{type.Value.Name}' command in the file is empty.");
            }

            metadata = metadata.WithCommand(command);
        }

        return Result.Success(metadata);
    }
}
