using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryEngine;
using System;
using System.Collections.Immutable;
using System.IO;
using Xunit;

namespace Fabolus.Tests.Fixtures;

public class GeometryEngineFixture
{
    public IGeometryEngine Engine { get; }

    public GeometryEngineFixture()
    {
        // The same factory the app uses, so the tests exercise the kernel that ships.
        Engine = BspGeometryEngine.Create();
    }

    /// <summary>
    /// Adds a mesh to the workspace under a fresh entry and makes it active, handing back the
    /// updated workspace and the entry's id. Identity lives on the record rather than the mesh,
    /// so a test that needs an id has to mint one - this is that, in one line.
    /// </summary>
    public static (Workspace Workspace, Guid Id) AddActive(Workspace workspace, IMesh mesh, string name = "test")
    {
        var record = MeshRecord.ForImport(name);
        return (workspace.AddMesh(mesh, record).Value, record.Id);
    }

    public IMesh LoadStl(string name)
    {
        var path = GetAssetPath(name);
        var result = Engine.IO.Import(path);
        if (result.IsFailure)
        {
            throw new Exception($"Failed to load STL: {result.Error.Description}");
        }
        return result.Value;
    }

    public string GetAssetPath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        // Search upward for the shared asset folder rather than hopping a fixed number of
        // levels: the output layout gains a directory when a platform is set
        // (bin/Release/net8.0 vs bin/x64/Release/net8.0), which a fixed count gets wrong.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "files", name);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find test asset '{name}' in any 'files' folder above '{AppContext.BaseDirectory}'.");
    }

    public IMesh UnitCube()
    {
        Vector3[] vertices = new Vector3[]
        {
            new(-0.5, -0.5, -0.5), // 0
            new( 0.5, -0.5, -0.5), // 1
            new( 0.5,  0.5, -0.5), // 2
            new(-0.5,  0.5, -0.5), // 3
            new(-0.5, -0.5,  0.5), // 4
            new( 0.5, -0.5,  0.5), // 5
            new( 0.5,  0.5,  0.5), // 6
            new(-0.5,  0.5,  0.5)  // 7
        };

        int[] triangles = new int[]
        {
            // Bottom (z = -0.5)
            0, 2, 1,
            0, 3, 2,
            // Top (z = 0.5)
            4, 5, 6,
            4, 6, 7,
            // Front (y = -0.5)
            0, 1, 5,
            0, 5, 4,
            // Back (y = 0.5)
            2, 3, 7,
            2, 7, 6,
            // Left (x = -0.5)
            0, 4, 7,
            0, 7, 3,
            // Right (x = 0.5)
            1, 2, 6,
            1, 6, 5
        };

        return Engine.CreateMesh(
            vertices.ToImmutableArray(),
            triangles.ToImmutableArray(),
            MeshMetadata.Named("unit cube")).Value;
    }
}

[CollectionDefinition("GeometryEngine collection")]
public class GeometryEngineCollection : ICollectionFixture<GeometryEngineFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}
