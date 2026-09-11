# Geometry.Manifold

An experimental replacement for `Geometry.MeshLib`, built on
[Manifold](https://github.com/elalish/manifold) 3.x through a hand-written binding to its C API.

It implements the same `IGeometryEngine` surface, returns the same error codes, and writes the
same metadata, so the two are interchangeable:

```csharp
IGeometryEngine engine = new GeometryManifold.GeometryEngine(fileSystem);
```

The native binding follows the one in [nsmela/meshcsg](https://github.com/nsmela/meshcsg)
(`MeshCsg.Engine/Internal/Native`): direct P/Invoke over `MeshGL64`, paired `alloc`/`delete`,
Manifold's own `merge` for welding, and a resolver that probes for the library rather than
assuming it is on the path.

## Running the test suite against it

`Fabolus.Core.Tests` targets whichever engine `FABOLUS_GEOMETRY_ENGINE` names. MeshLib stays the
default, so nothing changes unless you ask for it:

```powershell
dotnet test tests/Fabolus.Core.Tests            # MeshLib (default)
$env:FABOLUS_GEOMETRY_ENGINE = 'manifold'
dotnet test tests/Fabolus.Core.Tests            # Manifold
```

All 141 tests pass on both.

## What Manifold does, and what it does not

Manifold is a much narrower library than MeshLib. It is a boolean kernel: exact, robust, and
guaranteed to hand back a closed solid. Everything MeshLib gave away around that — file loaders,
spatial queries, decimation, repair — it simply does not have, so a good half of this project is
managed code that fills those gaps.

| Operation | How it is done here |
| --- | --- |
| Union / Subtract / Intersect | Manifold booleans. Exact, and always manifold — no error string to check afterwards. |
| Extrude polygon | `manifold_extrude`, which triangulates the caps and closes the solid itself. |
| Offset / OffsetDouble | `manifold_level_set` over a signed distance field sampled from `MeshBvh`. |
| Repair self-intersections | A self-union, which re-cuts every crossing surface. |
| Translate / Scale / Rotate | Plain arithmetic on the vertex array — a transform cannot break topology, and routing it through a native handle would reject the open meshes the pipeline legitimately carries. |
| Statistics, normals, topology | Computed directly from the vertex and triangle arrays. |
| Import / Export | `Internal/MeshFiles.cs`: STL (binary and ASCII), OBJ, OFF, PLY, plus a 3MF reader and writer carrying the command history and base mesh. |
| Raycast, closest point, signed distance | `Internal/MeshBvh.cs`. |
| Decimation (`Resize`) | `Internal/MeshDecimator.cs`, quadric edge collapse. |
| Planar triangulation | `Internal/PolygonTriangulator.cs`, ear clipping plus Delaunay flips. |
| Self-intersection count | `Internal/TriangleIntersection.cs`, Möller's triangle-triangle test over a BVH broad phase. |
| 2D polygon offset / union / buffer | Clipper2, exactly as before — it was never MeshLib's job. |
| Mesh shadow / convex hull | NetTopologySuite, exactly as before. |

## Things worth knowing

**The binding is hand-written, not the `ManifoldNET` package.** That package was tried first and
abandoned. Its `Dispose` frees the handle block with `FreeHGlobal` but never calls the matching
`manifold_delete_*`, so the C++ destructor never runs and every solid leaks its vertex and
triangle buffers — not survivable in an app that runs booleans all session. It also binds the
float `MeshGL` rather than the double-precision `MeshGL64` used here, and pins Manifold 2.5,
whose level-set mesher is both slower and coarser than 3.x's.

**Everything gets welded.** Manifold decides manifoldness from shared vertex indices alone. An
STL has no index buffer, so its importer emits three fresh vertices per triangle and even a
perfect cube comes back `NotManifold`. On the native side `manifold_meshgl64_merge` does this and
the result records that it happened (`CreatedBy` reads "operands merged"), because the merge
closes the mesh by moving geometry. The managed paths — topology validation, statistics, import —
use `MeshExtensions.Weld` for the same reason without going near a native handle.

**A C++ exception from Manifold kills the process.** It cannot cross the P/Invoke boundary, so it
calls `terminate` instead. Degenerate input is therefore not merely a bad result: feeding the
boolean a self-intersecting mesh aborted the whole test host until the decimator was fixed to
stop producing them. Anything new that hands geometry to the kernel should keep that in mind.

**Triangulation quality matters more than it looks.** The decal builder lifts each 2D vertex onto
a curved surface independently, so a sliver triangle spanning a whole wrapped label cuts through
the geometry beside it. Plain ear clipping produced exactly that; picking the roundest ear and
then flipping to Delaunay is what fixes it.

**No native binaries ship with this project.** `ManifoldNative` probes for `manifoldc` beside the
assembly, beside the host, and in the NuGet `runtimes/<rid>/native` layout, and reports
`Manifold.Unavailable` rather than throwing when it finds nothing. Building the app needs
`manifoldc.dll` (Manifold 3.x, win-x64) dropped into the output; a Linux or macOS build needs the
corresponding `.so`/`.dylib`. Built from source with:

```bash
cmake -B build -DCMAKE_BUILD_TYPE=Release -DMANIFOLD_CBIND=ON -DMANIFOLD_PAR=ON -DBUILD_SHARED_LIBS=ON
cmake --build build -j
```

**`Fabolus.Core` still references MeshLib.** The package reference is in `Fabolus.Core.csproj`
even though no `MR.` type is used there, so this project pulls MeshLib in transitively. Dropping
that reference is a separate cleanup.

**The app still wires up MeshLib.** `App.xaml.cs` registers
`GeometryMeshLib.GeometryEngine` for `IGeometryEngine`; switching is that one line plus the four
design-time constructors in the Cut/Mould/Rotate/Smoothing view models.
