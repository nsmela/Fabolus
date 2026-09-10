# Geometry.Manifold

An experimental replacement for `Geometry.MeshLib`, built on
[Manifold](https://github.com/elalish/manifold) through the
[ManifoldNET](https://www.nuget.org/packages/ManifoldNET) binding.

It implements the same `IGeometryEngine` surface, returns the same error codes, and writes the
same metadata, so the two are interchangeable:

```csharp
IGeometryEngine engine = new GeometryManifold.GeometryEngine(fileSystem);
```

## Running the test suite against it

`Fabolus.Core.Tests` targets whichever engine `FABOLUS_GEOMETRY_ENGINE` names. MeshLib stays the
default, so nothing changes unless you ask for it:

```powershell
dotnet test tests/Fabolus.Core.Tests            # MeshLib (default)
$env:FABOLUS_GEOMETRY_ENGINE = 'manifold'
dotnet test tests/Fabolus.Core.Tests            # Manifold
```

## What Manifold does, and what it does not

Manifold is a much narrower library than MeshLib. It is a boolean kernel: exact, robust, and
guaranteed to hand back a closed solid. Everything MeshLib gave away around that - file loaders,
spatial queries, decimation, repair - it simply does not have, so a good half of this project is
managed code that fills those gaps.

| Operation | How it is done here |
| --- | --- |
| Union / Subtract / Intersect | Manifold booleans. Exact, and always manifold - no error string to check afterwards. |
| Extrude polygon | `Manifold.Extrude`, which triangulates the caps and closes the solid itself. |
| Offset / OffsetDouble | `MeshGL.LevelSet` over a signed distance field sampled from `MeshBvh`. |
| Repair self-intersections | A self-union, which re-cuts every crossing surface. |
| Translate / Scale / Rotate | Plain arithmetic on the vertex array - a transform cannot break topology, and routing it through a native handle would reject the open meshes the pipeline legitimately carries. |
| Statistics, normals, topology | Computed directly from the vertex and triangle arrays. |
| Import / Export | `Internal/MeshFiles.cs`: STL (binary and ASCII), OBJ, OFF, PLY, plus a 3MF reader and writer carrying the command history and base mesh. |
| Raycast, closest point, signed distance | `Internal/MeshBvh.cs`. |
| Decimation (`Resize`) | `Internal/MeshDecimator.cs`, quadric edge collapse. |
| Planar triangulation | `Internal/PolygonTriangulator.cs`, ear clipping plus Delaunay flips. |
| Self-intersection count | `Internal/TriangleIntersection.cs`, Moller's triangle-triangle test over a BVH broad phase. |
| 2D polygon offset / union / buffer | Clipper2, exactly as before - it was never MeshLib's job. |
| Mesh shadow / convex hull | NetTopologySuite, exactly as before. |

## Things worth knowing

**Everything gets welded.** Manifold decides manifoldness from shared vertex indices alone. An STL
has no index buffer, so its importer emits three fresh vertices per triangle and even a perfect
cube comes back `NotManifold`. `MeshExtensions.Weld` merges coincident vertices on the way in;
without it nothing works.

**Offsetting is slower than MeshLib's.** The level set costs one closest-point query per voxel,
through a managed callback. Offsetting `sphere.stl` by 2mm takes about 6.5s against MeshLib's
0.14s. `DefaultOffsetResolution` in `GeometryModifiers` trades accuracy against that.

**Triangulation quality matters more than it looks.** The decal builder lifts each 2D vertex onto
a curved surface independently, so a sliver triangle spanning a whole wrapped label cuts through
the geometry beside it. Plain ear clipping produced exactly that; picking the roundest ear and
then flipping to Delaunay is what fixes it.

**ManifoldNET ships win-x64 natives only.** The package has no `runtimes/linux-x64` or `osx`
payload, though its MSBuild targets look for them. That is fine for the app, which is Windows-only,
but a Linux or macOS build needs `manifoldc.so` built from source and dropped next to the
assembly.

**`Fabolus.Core` still references MeshLib.** The package reference is in `Fabolus.Core.csproj`
even though no `MR.` type is used there, so this project pulls MeshLib in transitively. Dropping
that reference is a separate cleanup.
