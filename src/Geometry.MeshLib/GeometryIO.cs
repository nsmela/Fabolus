using Fabolus.Core.Common;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Core.Features.MeshIO;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json;
using System.Linq;

namespace GeometryMeshLib;

internal sealed class GeometryIO : IGeometryIO
{
    private static readonly string[] SupportedImportFormats = { ".stl", ".obj", ".off", ".ply", ".3mf" };

    private readonly IFileSystem _fileSystem;
    private readonly GeometryEngine _engine;

    public GeometryIO(IFileSystem fileSystem, GeometryEngine engine)
    {
        _fileSystem = fileSystem;
        _engine = engine;
    }

    private void Handle3MFExport(IMesh mesh, string tempFile)
    {
        string baseTempFile = null;
        XElement baseObject = null;
        XNamespace ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

        // 1. If base mesh exists, extract its <object>. GetBaseMeshCopy hands out an owned
        // copy (never the stored instance), disposed as soon as it's been written out.
        var baseCopyResult = mesh.Metadata.GetBaseMeshCopy();
        if (baseCopyResult.HasValue)
        {
            using var baseCopy = baseCopyResult.Value;
            if (baseCopy is MRMesh baseMrMesh)
            {
                baseTempFile = Path.GetTempFileName() + ".3mf";
                MR.MeshSave.toAnySupportedFormat(baseMrMesh.Mesh, baseTempFile, null);
                using (var baseArchive = ZipFile.OpenRead(baseTempFile))
                {
                    var modelEntry = baseArchive.GetEntry("3D/3dmodel.model");
                    if (modelEntry != null)
                    {
                        using var stream = modelEntry.Open();
                        var xdoc = XDocument.Load(stream);
                        baseObject = xdoc.Root?.Element(ns + "resources")?.Element(ns + "object");
                    }
                }
            }
        }

        // 2. Modify the main 3MF file's 3D/3dmodel.model
        using (var mainArchive = ZipFile.Open(tempFile, ZipArchiveMode.Update))
        {
            var modelEntry = mainArchive.GetEntry("3D/3dmodel.model");
            if (modelEntry != null)
            {
                XDocument mainDoc;
                using (var stream = modelEntry.Open())
                {
                    mainDoc = XDocument.Load(stream);
                }

                if (mainDoc.Root != null)
                {
                    // Add JSON command history as standard <metadata> with custom namespace to satisfy strict 3MF rules
                    XNamespace fabNs = "http://fabolus.io/2026/metadata";
                    mainDoc.Root.Add(new XAttribute(XNamespace.Xmlns + "fab", fabNs.NamespaceName));

                    var commandRecords = mesh.Metadata.Commands.Select(c => new
                    {
                        Type = c.GetType().Name,
                        Data = (object)c
                    });
                    string json = JsonSerializer.Serialize(commandRecords, new JsonSerializerOptions { WriteIndented = false, IncludeFields = true });
                    mainDoc.Root.AddFirst(new XElement(ns + "metadata", new XAttribute("name", "fab:Commands"), json));

                    // Add base object to resources
                    if (baseObject != null)
                    {
                        var resources = mainDoc.Root.Element(ns + "resources");
                        if (resources != null)
                        {
                            var existingIds = resources.Elements()
                                .Select(e => e.Attribute("id")?.Value)
                                .Where(v => v != null)
                                .Select(v => int.TryParse(v, out int id) ? id : 0)
                                .ToList();
                            
                            int newId = existingIds.Count > 0 ? existingIds.Max() + 1 : 1;
                            baseObject.SetAttributeValue("id", newId.ToString());
                            baseObject.SetAttributeValue("type", "other"); // Prevents slicer errors for unreferenced models
                            baseObject.SetAttributeValue(fabNs + "role", "basemesh"); // Explicit and foolproof identifier
                            
                            // Remove any conflicting materials or colors (we just want the geometry)
                            foreach (var attr in baseObject.Descendants(ns + "triangle").Attributes("pid").ToList())
                                attr.Remove();
                            foreach (var attr in baseObject.Descendants(ns + "triangle").Attributes("p1").ToList())
                                attr.Remove();

                            resources.Add(baseObject);
                        }
                    }

                    // Save the modified XML safely
                    modelEntry.Delete();
                    modelEntry = mainArchive.CreateEntry("3D/3dmodel.model");
                    using (var stream = modelEntry.Open())
                    using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings { Encoding = new System.Text.UTF8Encoding(false), OmitXmlDeclaration = false }))
                    {
                        mainDoc.Save(writer);
                    }
                }
            }
        }

        if (baseTempFile != null && File.Exists(baseTempFile))
        {
            File.Delete(baseTempFile);
        }
    }

    private Result<IMesh> Import3MF(string filePath)
    {
        try
        {
            var metadata = MeshMetadata.FromFileName(filePath);
            
            var commandTypes = typeof(IMeshCommand).Assembly.GetTypes()
                .Where(t => typeof(IMeshCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToDictionary(t => t.Name, t => t);

            XNamespace ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
            XDocument xdoc;

            using (var archive = ZipFile.OpenRead(filePath))
            {
                var modelEntry = archive.GetEntry("3D/3dmodel.model");
                if (modelEntry == null) return IOErrors.NoMeshData;

                using (var stream = modelEntry.Open())
                {
                    xdoc = XDocument.Load(stream);
                }
            }

            if (xdoc.Root != null)
            {
                // 1. Read JSON command history from standard 3MF <metadata> element
                var metadataElem = xdoc.Root.Elements(ns + "metadata")
                    .FirstOrDefault(e => string.Equals(e.Attribute("name")?.Value, "fab:Commands", StringComparison.OrdinalIgnoreCase));
                
                if (metadataElem != null && !string.IsNullOrWhiteSpace(metadataElem.Value))
                {
                    try
                    {
                        var commandRecords = JsonSerializer.Deserialize<JsonElement[]>(metadataElem.Value);
                        if (commandRecords != null)
                        {
                            foreach (var record in commandRecords)
                            {
                                if (record.TryGetProperty("Type", out var typeElement) &&
                                    record.TryGetProperty("Data", out var dataElement))
                                {
                                    string typeName = typeElement.GetString() ?? "";
                                    if (commandTypes.TryGetValue(typeName, out var type))
                                    {
                                        var cmd = (IMeshCommand?)JsonSerializer.Deserialize(dataElement.GetRawText(), type, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, IncludeFields = true });
                                        if (cmd != null)
                                        {
                                            metadata = metadata.WithCommand(cmd);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { /* Ignore parsing errors */ }
                }
            }

            var resources = xdoc.Root?.Element(ns + "resources");
            if (resources == null) return IOErrors.NoMeshData;

            var objects = resources.Elements(ns + "object")
                .Where(o => o.Element(ns + "mesh") != null) // Only consider objects that actually contain a mesh
                .ToList();

            if (objects.Count == 0) return IOErrors.NoMeshData;

            XNamespace fabNs = "http://fabolus.io/2026/metadata";

            // Find base object explicitly by custom role or type
            var baseObject = objects.FirstOrDefault(o => 
                string.Equals(o.Attribute(fabNs + "role")?.Value, "basemesh", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(o.Attribute("type")?.Value, "other", StringComparison.OrdinalIgnoreCase));

            // Main object is whatever has a mesh and is NOT the base object
            var mainObject = objects.FirstOrDefault(o => o != baseObject);

            if (mainObject == null) 
            {
                // Fallback in case there's only one object and it was mistakenly flagged as base
                mainObject = objects.First();
                if (mainObject == baseObject) baseObject = null;
            }

            // Load main mesh
            string mainTemp = Path.GetTempFileName() + ".obj";
            try
            {
                WriteObjectToObj(mainObject, ns, mainTemp);
                var loadedMesh = MR.MeshLoad.fromAnySupportedFormat(mainTemp, null);
                if (loadedMesh is null) return IOErrors.NoMeshData;
                loadedMesh.pack();
                
                // Load base mesh if exists
                if (baseObject != null)
                {
                    string baseTemp = Path.GetTempFileName() + ".obj";
                    try
                    {
                        WriteObjectToObj(baseObject, ns, baseTemp);
                        var baseLoadedMesh = MR.MeshLoad.fromAnySupportedFormat(baseTemp, null);
                        if (baseLoadedMesh is not null)
                        {
                            baseLoadedMesh.pack();
                            metadata = metadata.WithBaseMesh(new MRMesh(baseLoadedMesh, MeshMetadata.FromFileName("BaseMesh")));
                        }
                    }
                    finally
                    {
                        if (File.Exists(baseTemp)) File.Delete(baseTemp);
                    }
                }

                var mrMesh = new MRMesh(loadedMesh, metadata);
                var validation = _engine.Evaluators.ValidateTopology(mrMesh);
                if (validation.IsSuccess)
                {
                    mrMesh = (MRMesh)mrMesh.WithMetadata(metadata.WithTopology(validation.Value));
                }

                return Result.Success<IMesh>(mrMesh);
            }
            finally
            {
                if (File.Exists(mainTemp)) File.Delete(mainTemp);
            }
        }
        catch (Exception ex)
        {
            throw;
        }
    }

    private void WriteObjectToObj(XElement objNode, XNamespace ns, string outputPath)
    {
        var meshNode = objNode.Element(ns + "mesh");
        if (meshNode == null)
            throw new System.Exception("WriteObjectToObj: meshNode is null. The object might be a <components> instead of a <mesh>! XML: " + objNode.ToString());

        var vertices = meshNode.Element(ns + "vertices")?.Elements(ns + "vertex");
        var triangles = meshNode.Element(ns + "triangles")?.Elements(ns + "triangle");
        
        if (vertices == null || triangles == null)
            throw new System.Exception($"WriteObjectToObj: vertices or triangles are null. vertices: {vertices != null}, triangles: {triangles != null}. XML: " + meshNode.ToString());

        using var writer = new StreamWriter(outputPath);
        foreach (var v in vertices)
        {
            var x = v.Attribute("x")?.Value;
            var y = v.Attribute("y")?.Value;
            var z = v.Attribute("z")?.Value;
            if (x == null || y == null || z == null)
                throw new System.Exception($"WriteObjectToObj: missing coordinate. x:{x} y:{y} z:{z}");
            writer.WriteLine($"v {x} {y} {z}");
        }
        foreach (var t in triangles)
        {
            // OBJ indices are 1-based
            if (int.TryParse(t.Attribute("v1")?.Value, out int v1) &&
                int.TryParse(t.Attribute("v2")?.Value, out int v2) &&
                int.TryParse(t.Attribute("v3")?.Value, out int v3))
            {
                writer.WriteLine($"f {v1 + 1} {v2 + 1} {v3 + 1}");
            }
        }
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
            if (extension == ".3mf")
            {
                return Import3MF(filePath);
            }

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
            throw;
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
                
                if (filePath.EndsWith(".3mf", StringComparison.OrdinalIgnoreCase))
                {
                    Handle3MFExport(mesh, tempFile);
                }

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
