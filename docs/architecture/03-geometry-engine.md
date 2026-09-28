# Geometry Engine

## The 3D Calculation Engine

Fabolus delegates every geometric operation — booleans, offsets, smoothing, decimation, spatial queries, polygon work and mesh files — to **[GeometryEngine](https://github.com/nsmela/GeometryEngine)**, a separate library with its own repository and test suite.

Keeping it separate is the point. Fabolus is clinical software with a user interface; GeometryEngine is a geometry library with no opinion about boluses or moulds. The boundary between them is a single interface, `IGeometryEngine`, which means the geometry can be tested exhaustively on its own — against analytic volume identities and real clinical meshes — without a workspace, a view model or a window in sight.

<!-- IMAGE_PLACEHOLDER: [Figure 12.1: Engine Boundary. Diagram showing Fabolus.Core calling IGeometryEngine, with the Manifold native kernel and the managed BSP fallback behind it. Dimensions: 900x450px.] -->

```csharp
public interface IGeometryEngine
{
    IBooleans Booleans { get; }              // union, subtract, intersect
    IGeometryGenerators Generators { get; }  // primitives, tubes, swept and draped paths
    IGeometryEvaluators Evaluators { get; }  // measurement and topology inspection
    IGeometryTransforms Transforms { get; }  // translate, scale, rotate
    IGeometryIO IO { get; }                  // mesh files and 3MF packages
    IGeometryModifiers Modifiers { get; }    // offsetting, smoothing, decimation, repair
    ISpatialQueries Spatial { get; }         // ray, closest-point and signed-distance queries
    IPolygonOperations Polygons { get; }     // planar outlines, offsets, unions, extrusion
    IDecalOperations Decals { get; }         // text and outlines turned into solids on a surface
}
```

---

## The Mesh Contract

The only mesh type is `ImmutableMesh`, and it cannot be constructed in an invalid state — `Create` rejects ragged index arrays, out-of-range indices and non-finite coordinates before anything else can see the mesh.

```csharp
public interface IMesh
{
    ImmutableArray<Vec3> Vertices { get; }
    ImmutableArray<int> Triangles { get; }
    MeshMetadata Metadata { get; }
    int VertexCount { get; }
    int TriangleCount { get; }
    bool IsEmpty { get; }
    IMesh WithMetadata(MeshMetadata metadata);
    (Vec3 A, Vec3 B, Vec3 C) TriangleAt(int triangleIndex);
}
```

Vertices and triangles are exposed as immutable arrays rather than `Vec3[]` and `int[]`, so handing a mesh to a caller cannot let them corrupt it. **A mesh is a value, not a resource**: it is not `IDisposable`, nothing needs a `using`, and two callers holding the same instance cannot interfere with each other.

This is a change worth flagging for anyone reading older code. Under the previous backend a mesh wrapped native C++ memory and had to be disposed, so the codebase was written around an ownership contract — "consumed inputs", "owned copies the caller must dispose". Comments in that idiom are leftovers wherever they survive; they describe a hazard that no longer exists.

The one thing that *is* still a resource is `ISpatialIndex`, which holds a native acceleration structure. Build one once and reuse it — building costs far more than querying — and dispose it when finished.

---

## Two Kernels, One Interface

Booleans and distance-field work run on **Manifold**, a native C++ kernel shipped alongside the library (with oneTBB for parallelism) under `src/GeometryEngine/runtimes/`. Where the native library cannot be loaded, the engine falls back to a fully managed BSP implementation rather than failing.

```csharp
BspGeometryEngine.Create();            // Manifold, falling back to managed BSP
BspGeometryEngine.CreateManagedBsp();  // managed only
```

Which kernel produced a given mesh is recorded in `MeshMetadata.CreatedBy`, so a result can always be traced back to the code that made it. Guarantees are explicit rather than assumed: a mesh labelled as native is always watertight.

---

## Key 3D Algorithms

### 1. Solid 3D Operations (CSG Booleans)
Boolean operations combine or subtract 3D shapes:
- **Subtract**: Carves the bolus cavity and air channel tunnels out of the solid mould block.
- **Union**: Merges separate 3D bodies into a single continuous, watertight solid.
- **Intersect**: Evaluates overlapping areas or trims meshes along a cutting plane.

The boolean engine handles complex medical meshes reliably, avoiding the crashes and surface inversion common in basic CAD tools. Note that it only re-meshes near the intersection: geometry away from where the operands meet passes through untouched, carrying any slivers or coincident vertices it already had. That is normal output and not a defect — only a hole makes a mesh unprintable.

### 2. Volume-Preserving Smoothing
Instead of simple surface blurring (which shrinks the model and thins bolus walls), Fabolus uses a **two-step offset**:
1. **Inflate**: Expands the surface outward by a set distance, filling the sharp stair-stepping gaps between CT slices.
2. **Deflate**: Contracts the surface back inward by the exact same distance.

This removes sharp ridges and corners while returning flat and broad regions to their planned thickness, ensuring the prescribed radiation dose is delivered accurately.

`Modifiers` offers several variants with genuinely different trade-offs, and the choice matters: `OffsetSmooth` runs the whole inflate/deflate cycle on one sampled distance field, where `DoubleOffset` re-meshes between every pass and compounds its losses. `SmoothCreases` rounds sharp folds while holding every other vertex within a guaranteed distance of where it started. The library's own API documentation records the measured volume drift for each on real bolus meshes.

### 3. Smooth Curved Air Channels
When generating curved or angled air channels, the engine sweeps circular rings along the channel's 3D path and connects them with triangle strips:
- Standard curve-following techniques often twist or pinch when a curve turns or flattens out.
- A stable orientation technique (known as a **Parallel Transport** or **Bishop frame**) keeps each circular cross-section aligned smoothly with the path.
- This produces clean, un-twisted cylindrical tubes that ensure silicone can enter and air can escape without obstruction.

<!-- IMAGE_PLACEHOLDER: [Figure 12.2: Parallel Transport Frame vs Standard Framing. Visual comparison showing how stable frame transport prevents twisting along curved 3D channel paths. Dimensions: 800x400px.] -->

### 4. 2D Mould Footprints via Clipper2
To create mould shells (Convex, Concave, or Contoured):
- The bottom outline of the bolus is projected downward onto a flat 2D plane.
- The 2D outline is expanded outward by the specified wall thickness using the **Clipper2** polygon library, with smooth rounded corners.
- The expanded 2D shape is then extruded vertically to create the mould walls, capped with flat top and bottom faces.

Offsets, buffers and unions all answer with a single outline: where an operation splits a region into islands the largest is kept, since every consumer of these wants one footprint.
