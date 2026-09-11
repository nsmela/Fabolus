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
| Offset / OffsetDouble | `manifold_level_set` over a signed distance field - from the `native/` libigl shim where it is present, from `MeshBvh` otherwise. |
| Repair self-intersections | A self-union, which re-cuts every crossing surface. |
| Translate / Scale / Rotate | Plain arithmetic on the vertex array — a transform cannot break topology, and routing it through a native handle would reject the open meshes the pipeline legitimately carries. |
| Statistics, normals, topology | Computed directly from the vertex and triangle arrays. |
| Import / Export | `Internal/MeshFiles.cs`: STL (binary and ASCII), OBJ, OFF, PLY, plus a 3MF reader and writer carrying the command history and base mesh. |
| Raycast, closest point, signed distance | `Internal/MeshBvh.cs`, or the libigl shim for batched distance queries. |
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

**The distance field can be native.** `native/` builds `fabolus_geometry`, a small libigl-backed
library that runs a whole offset - level-set callback included - behind one call, instead of
paying a P/Invoke transition per sample. It is optional: without it the managed `MeshBvh` path
computes the same field, and the suite passes either way. With it the Manifold suite runs in 23s
rather than 1m33s, and the distances agree to within float rounding. See `native/README.md`,
which also records why neither of libigl's own `signed_distance` helpers is used directly.

**The win-x64 natives are vendored; nothing else is.** `runtimes/win-x64/native/` holds
`manifoldc.dll` and `manifold.dll`, and the project copies them flat into every build output -
including, transitively, the app's and the test project's. `ManifoldNative` probes beside the
assembly, beside the host, and in the `runtimes/<rid>/native` layout, and reports
`Manifold.Unavailable` rather than throwing when it finds nothing. A Linux or macOS build has to
supply its own `.so`/`.dylib`; see `runtimes/README.md` for the build recipe.

Those two DLLs came from `nsmela/meshcsg` byte for byte, and their build provenance is not
established - they carry no version resource. What *is* established is that their C API is
exactly Manifold 3.5.1's: the exported symbol sets match a 3.5.1 build from source precisely,
293 entry points with no difference either way. `runtimes/README.md` records the checksums, what
is still unknown, and why that matters under IEC 62304.

**`Fabolus.Core` still references MeshLib.** The package reference is in `Fabolus.Core.csproj`
even though no `MR.` type is used there, so this project pulls MeshLib in transitively. Dropping
that reference is a separate cleanup.

**The app still wires up MeshLib.** `App.xaml.cs` registers
`GeometryMeshLib.GeometryEngine` for `IGeometryEngine`; switching is that one line plus the four
design-time constructors in the Cut/Mould/Rotate/Smoothing view models.
