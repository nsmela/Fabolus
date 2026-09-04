# Geometry Engine & Native MeshLib

## The Native Kernel: MeshLib (MeshInspector)

Fabolus delegates compute-intensive mesh algorithms to **MeshLib** (v3.1.2.192), a high-performance C++ geometric modeling kernel developed by MeshInspector.

MeshLib is consumed via official .NET bindings and packaged within `Geometry.MeshLib`.

---

## Memory Management & Safety Contracts

C++ native objects (`MR.Mesh`, `MR.MeshPart`, `MR.OffsetParameters`) allocate unmanaged memory on the native heap. To prevent memory leaks while maintaining garbage collection safety in .NET:

1. **The `MRMesh` Boundary**:
   - The core domain only ever sees the pure managed interface [`IMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IMesh.cs):
     ```csharp
     public interface IMesh {
         Vector3[] Vertices { get; }
         int[] Triangles { get; }
         MeshMetadata Metadata { get; }
     }
     ```
   - `MRMesh` implements `IMesh` internally. Native pointers never escape into `Fabolus.Core` or `Fabolus.Wpf`.
2. **Deterministic Disposal**:
   - All intermediate native representations inside `GeometryModifiers`, `Booleans`, and `GeometryGenerators` are wrapped in `using` blocks:
     ```csharp
     using var model = input.ToMRMesh();
     using var mp = new MR.MeshPart(model);
     using var result = MR.offsetMesh(mp, offsetDistance, parms);
     return Result.Success(result.ToIMesh(newMetadata));
     ```
   - When native algorithms complete, unmanaged allocations are immediately freed.

---

## Key Algorithmic Subsystems

### 1. Robust Boolean CSG Operations ([`Booleans.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/Booleans.cs))
- Uses exact arithmetic and adaptive octree intersection to execute:
  - **Union**: Merging components into a single outer envelope.
  - **Difference (Subtract)**: Cavity coring and air channel clearance.
  - **Intersection**: Volume overlap calculation and cutting planes.

### 2. Morphological Offsets ([`GeometryModifiers.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryModifiers.cs))
- Executes `MR.doubleOffsetMesh`:
  $$\mathcal{M}_{\text{closed}} = \mathcal{M} \oplus d \ominus d$$
- Operates on signed distance fields (SDF) sampled onto continuous voxel grids. This eliminates topological self-intersections and collapses narrow grooves without shrinking outer features.

### 3. Swept 3D Tubes & Air Channels ([`GeometryGenerators.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryGenerators.cs#L18))
- Generates cylindrical and conical tubes along arbitrary 3D polylines.
- Implements a **Parallel Transport Frame (Bishop Frame)** to rotate orthogonal coordinate vectors along the tangent curve, avoiding the unwanted gimbal twists inherent in standard Frenet-Serret frames.

### 4. 2D Polygon Offsets via Clipper2Lib
- To construct convex hull and shadow projections for sacrificial moulds:
  - Mesh silhouettes are projected onto the $XY$ plane.
  - Polygons are expanded with round or miter joins using `Clipper2Lib`.
  - The expanded 2D contours are extruded vertically with ear-clipping triangulation for top and bottom end-caps.
