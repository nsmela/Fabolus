using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.MeshIO;

namespace GeometryMeshLib;

internal sealed class GeometryIO : IGeometryIO
{
    private static readonly string[] SupportedImportFormats = { ".stl", ".obj", ".off", ".ply" };

    private readonly IFileSystem _fileSystem;
    private readonly GeometryEngine _engine;

    public GeometryIO(IFileSystem fileSystem, GeometryEngine engine)
    {
        _fileSystem = fileSystem;
        _engine = engine;
    }

    public Result<IMesh> Import(string filePath)
    {
        if (!_fileSystem.Exists(filePath))
            return IOErrors.FileNotFound(filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!SupportedImportFormats.Contains(extension))
            return IOErrors.UnsupportedFormat(extension, SupportedImportFormats);

        try
        {
            var loadedMesh = MR.MeshLoad.fromAnySupportedFormat(filePath, null);
            if (loadedMesh is null)
                return IOErrors.NoMeshData;

            loadedMesh.pack();

            var metadata = MeshMetadata.FromFileName(filePath);
            var mrMesh = new MRMesh(loadedMesh, metadata);

            var validation = _engine.Evaluators.ValidateTopology(mrMesh);
            if (validation.IsSuccess)
            {
                mrMesh = (MRMesh)mrMesh.WithMetadata(metadata.WithTopology(validation.Value));
            }

            return Result.Success<IMesh>(mrMesh);
        }
        catch (Exception ex)
        {
            return IOErrors.ReadFailed(ex.Message);
        }
    }

    public Result Export(IMesh mesh, string filePath, bool overwrite = false)
    {
        if (mesh is not MRMesh mrMesh)
            return IOErrors.InvalidMeshType;

        if (_fileSystem.Exists(filePath) && !overwrite)
            return IOErrors.FileExists(filePath);

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
                _fileSystem.CreateDirectory(directory);

            // Use a temporary file to leverage MeshLib's native exporters
            // while still enforcing the IFileSystem abstraction for the final destination.
            string tempFile = Path.GetTempFileName() + Path.GetExtension(filePath);
            try
            {
                MR.MeshSave.toAnySupportedFormat(mrMesh.Mesh, tempFile, null);
                var bytes = File.ReadAllBytes(tempFile);
                _fileSystem.WriteAllBytes(filePath, bytes);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }

            return Result.Success();
        }
        catch (UnauthorizedAccessException ex)
        {
            return IOErrors.AccessDenied(filePath, ex.Message);
        }
        catch (IOException ex)
        {
            return IOErrors.WriteFailed(ex.Message);
        }
        catch (Exception ex)
        {
            return IOErrors.WriteException(ex.Message);
        }
    }
}
