using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;
using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace GeometryManifold;

/// <summary>
/// Mesh file import and export. MeshLib supplied native loaders for all of these; Manifold has no
/// file I/O whatsoever, so the formats are read and written here - including 3MF, which carries
/// the app's command history and base mesh alongside the geometry.
/// </summary>
internal sealed class GeometryIO : IGeometryIO
{
    private static readonly string[] SupportedImportFormats = { ".stl", ".obj", ".off", ".ply", ".3mf" };

    private static readonly XNamespace CoreNs = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private static readonly XNamespace FabolusNs = "http://fabolus.io/2026/metadata";

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

        if (extension == ".3mf")
            return Import3MF(filePath);

        Vector3[] vertices;
        int[] triangles;
        try
        {
            (vertices, triangles) = MeshFiles.Read(filePath, _fileSystem.ReadAllBytes(filePath));
        }
        catch (Exception ex)
        {
            return IOErrors.ReadFailed(ex.Message);
        }

        if (triangles.Length == 0) return IOErrors.NoMeshData;

        return Finish(vertices, triangles, MeshMetadata.FromFileName(filePath));
    }

    public Result Export(IMesh mesh, string filePath, bool overwrite = false)
    {
        if (_fileSystem.Exists(filePath) && !overwrite)
            return IOErrors.FileExists(filePath);

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
                _fileSystem.CreateDirectory(directory);

            var bytes = filePath.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase)
                ? Write3MF(mesh)
                : MeshFiles.Write(filePath, mesh.Vertices, mesh.Triangles);

            _fileSystem.WriteAllBytes(filePath, bytes);
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

    /// <summary>
    /// Welds, compacts and validates freshly-read geometry, mirroring what MeshLib's loaders did
    /// implicitly. Without the weld an imported STL is a triangle soup and everything downstream -
    /// booleans especially - refuses it.
    /// </summary>
    private Result<IMesh> Finish(Vector3[] vertices, int[] triangles, MeshMetadata metadata)
    {
        var (welded, weldedTriangles) = MeshExtensions.Weld(vertices, triangles);
        var (compacted, compactedTriangles) = MeshExtensions.Compact(welded, weldedTriangles);

        IMesh mesh = new ManifoldMesh(compacted, compactedTriangles, metadata);

        var validation = _engine.Evaluators.ValidateTopology(mesh);
        if (validation.IsSuccess)
        {
            mesh = mesh.WithMetadata(metadata.WithTopology(validation.Value));
        }

        return Result.Success(mesh);
    }

    // ===== 3MF =====

    private byte[] Write3MF(IMesh mesh)
    {
        var model = new XElement(CoreNs + "model",
            new XAttribute("unit", "millimeter"),
            new XAttribute(XNamespace.Xmlns + "fab", FabolusNs.NamespaceName));

        // Command history rides in a standard <metadata> element under a custom namespace, which
        // keeps strict 3MF consumers happy while remaining ours to read back.
        var commandRecords = mesh.Metadata.Commands.Select(c => new
        {
            Type = MeshCommandRegistry.GetName(c),
            Data = (object)c,
        });
        string commandsJson = JsonSerializer.Serialize(commandRecords, new JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
        model.Add(new XElement(CoreNs + "metadata", new XAttribute("name", "fab:Commands"), commandsJson));

        var resources = new XElement(CoreNs + "resources");
        resources.Add(BuildObject(1, mesh.Vertices, mesh.Triangles, isBaseMesh: false));

        var baseMesh = mesh.Metadata.GetBaseMesh();
        if (baseMesh.HasValue)
        {
            resources.Add(BuildObject(2, baseMesh.Value.Vertices, baseMesh.Value.Triangles, isBaseMesh: true));
        }

        model.Add(resources);
        model.Add(new XElement(CoreNs + "build", new XElement(CoreNs + "item", new XAttribute("objectid", "1"))));

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var relationships = archive.CreateEntry("_rels/.rels");
            using (var stream = relationships.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Target=\"/3D/3dmodel.model\" Id=\"rel0\" " +
                    "Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\" />" +
                    "</Relationships>");
            }

            var contentTypes = archive.CreateEntry("[Content_Types].xml");
            using (var stream = contentTypes.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
                    "<Default Extension=\"model\" ContentType=\"application/vnd.ms-package.3dmanufacturing-3dmodel+xml\" />" +
                    "</Types>");
            }

            var modelEntry = archive.CreateEntry("3D/3dmodel.model");
            using (var stream = modelEntry.Open())
            using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false }))
            {
                new XDocument(model).Save(writer);
            }
        }

        return buffer.ToArray();
    }

    private static XElement BuildObject(int id, Vector3[] vertices, int[] triangles, bool isBaseMesh)
    {
        var verticesElement = new XElement(CoreNs + "vertices");
        foreach (var v in vertices)
        {
            verticesElement.Add(new XElement(CoreNs + "vertex",
                new XAttribute("x", v.X.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("y", v.Y.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("z", v.Z.ToString("R", CultureInfo.InvariantCulture))));
        }

        var trianglesElement = new XElement(CoreNs + "triangles");
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            trianglesElement.Add(new XElement(CoreNs + "triangle",
                new XAttribute("v1", triangles[i]),
                new XAttribute("v2", triangles[i + 1]),
                new XAttribute("v3", triangles[i + 2])));
        }

        var element = new XElement(CoreNs + "object",
            new XAttribute("id", id.ToString(CultureInfo.InvariantCulture)),
            new XElement(CoreNs + "mesh", verticesElement, trianglesElement));

        if (isBaseMesh)
        {
            // "other" keeps slicers from treating the unreferenced base copy as printable; the
            // fab:role attribute is what the importer actually keys on.
            element.SetAttributeValue("type", "other");
            element.SetAttributeValue(FabolusNs + "role", "basemesh");
        }
        else
        {
            element.SetAttributeValue("type", "model");
        }

        return element;
    }

    private Result<IMesh> Import3MF(string filePath)
    {
        var metadata = MeshMetadata.FromFileName(filePath);
        XDocument document;

        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            var modelEntry = archive.GetEntry("3D/3dmodel.model");
            if (modelEntry is null) return IOErrors.NoMeshData;

            using var stream = modelEntry.Open();
            document = XDocument.Load(stream);
        }
        catch (Exception ex)
        {
            return IOErrors.ReadFailed(ex.Message);
        }

        if (document.Root is null) return IOErrors.NoMeshData;

        var commandsResult = ReadCommands(document.Root, metadata);
        if (commandsResult.IsFailure) return commandsResult.Error;
        metadata = commandsResult.Value;

        var resources = document.Root.Element(CoreNs + "resources");
        if (resources is null) return IOErrors.NoMeshData;

        var objects = resources.Elements(CoreNs + "object")
            .Where(o => o.Element(CoreNs + "mesh") is not null)
            .ToList();

        if (objects.Count == 0) return IOErrors.NoMeshData;

        var baseObject = objects.FirstOrDefault(o =>
            string.Equals(o.Attribute(FabolusNs + "role")?.Value, "basemesh", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.Attribute("type")?.Value, "other", StringComparison.OrdinalIgnoreCase));

        var mainObject = objects.FirstOrDefault(o => o != baseObject);
        if (mainObject is null)
        {
            // Only one object, and it looked like a base: it is the model after all.
            mainObject = objects[0];
            if (mainObject == baseObject) baseObject = null;
        }

        try
        {
            if (baseObject is not null)
            {
                var (baseVertices, baseTriangles) = ReadObjectGeometry(baseObject);
                var (weldedBase, weldedBaseTriangles) = MeshExtensions.Weld(baseVertices, baseTriangles);
                var (compactBase, compactBaseTriangles) = MeshExtensions.Compact(weldedBase, weldedBaseTriangles);
                metadata = metadata.WithBaseMesh(
                    new ManifoldMesh(compactBase, compactBaseTriangles, MeshMetadata.FromFileName("BaseMesh")));
            }

            var (vertices, triangles) = ReadObjectGeometry(mainObject);
            if (triangles.Length == 0) return IOErrors.NoMeshData;

            return Finish(vertices, triangles, metadata);
        }
        catch (Exception ex)
        {
            return IOErrors.ReadFailed(ex.Message);
        }
    }

    /// <summary>
    /// Replays the stored command history onto the metadata. A command that fails to load is a
    /// hard failure, never a skip: the mesh's geometry already has it baked in, so dropping it
    /// leaves the history disagreeing with the model and every replay-from-base view renders
    /// something other than what was imported.
    /// </summary>
    private static Result<MeshMetadata> ReadCommands(XElement root, MeshMetadata metadata)
    {
        var element = root.Elements(CoreNs + "metadata")
            .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, "fab:Commands", StringComparison.OrdinalIgnoreCase));

        if (element is null || string.IsNullOrWhiteSpace(element.Value)) return Result.Success(metadata);

        JsonElement[]? records;
        try
        {
            records = JsonSerializer.Deserialize<JsonElement[]>(element.Value);
        }
        catch (JsonException ex)
        {
            return IOErrors.ReadFailed($"Command history is not valid JSON: {ex.Message}");
        }

        foreach (var record in records ?? Array.Empty<JsonElement>())
        {
            if (!record.TryGetProperty("Type", out var typeElement) ||
                !record.TryGetProperty("Data", out var dataElement))
            {
                return IOErrors.ReadFailed("A command history entry is missing its Type or Data.");
            }

            var typeResult = MeshCommandRegistry.ResolveType(typeElement.GetString() ?? string.Empty);
            if (typeResult.IsFailure) return typeResult.Error;

            IMeshCommand? command;
            try
            {
                command = (IMeshCommand?)JsonSerializer.Deserialize(
                    dataElement.GetRawText(),
                    typeResult.Value,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
            }
            catch (JsonException ex)
            {
                return IOErrors.ReadFailed($"Could not read the '{typeResult.Value.Name}' command: {ex.Message}");
            }

            if (command is null)
            {
                return IOErrors.ReadFailed($"The '{typeResult.Value.Name}' command in the file is empty.");
            }

            metadata = metadata.WithCommand(command);
        }

        return Result.Success(metadata);
    }

    private static (Vector3[] Vertices, int[] Triangles) ReadObjectGeometry(XElement objectElement)
    {
        var meshElement = objectElement.Element(CoreNs + "mesh")
            ?? throw new InvalidDataException("A 3MF object has no <mesh> - components are not supported.");

        var vertexElements = meshElement.Element(CoreNs + "vertices")?.Elements(CoreNs + "vertex")
            ?? throw new InvalidDataException("A 3MF mesh has no <vertices>.");
        var triangleElements = meshElement.Element(CoreNs + "triangles")?.Elements(CoreNs + "triangle")
            ?? throw new InvalidDataException("A 3MF mesh has no <triangles>.");

        var vertices = new List<Vector3>();
        foreach (var vertex in vertexElements)
        {
            vertices.Add(new Vector3(
                Attribute(vertex, "x"),
                Attribute(vertex, "y"),
                Attribute(vertex, "z")));
        }

        var triangles = new List<int>();
        foreach (var triangle in triangleElements)
        {
            triangles.Add(Index(triangle, "v1"));
            triangles.Add(Index(triangle, "v2"));
            triangles.Add(Index(triangle, "v3"));
        }

        return (vertices.ToArray(), triangles.ToArray());
    }

    private static float Attribute(XElement element, string name) =>
        float.Parse(
            element.Attribute(name)?.Value ?? throw new InvalidDataException($"A 3MF vertex is missing its '{name}'."),
            CultureInfo.InvariantCulture);

    private static int Index(XElement element, string name) =>
        int.Parse(
            element.Attribute(name)?.Value ?? throw new InvalidDataException($"A 3MF triangle is missing its '{name}'."),
            CultureInfo.InvariantCulture);
}
