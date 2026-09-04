# Geometry Engine & Native MeshLib

## The Native Computational Core

Fabolus delegates compute-intensive polygonal and volumetric operations to **MeshLib** (v3.1.2.192), a state-of-the-art computational geometry library engineered in C++ by MeshInspector.

MeshLib is linked through official .NET Interop bindings and wrapped entirely within the `Geometry.MeshLib` project.

<!-- IMAGE_PLACEHOLDER: [Figure 12.1: Managed C# to Native C++ Marshaling Lifecycle. Memory layout diagram contrasting managed heap arrays with native unmanaged C++ heap structs and deterministic disposal boundaries. Dimensions: 900x450px.] -->

---

## Memory Safety & Unmanaged Lifecycle Management

In .NET, mixing unmanaged native C++ pointers with the Garbage Collector (GC) introduces risks of memory corruption, double-frees, or severe memory leaks. Fabolus enforces strict safety contracts:

### 1. The `MRMesh` Encapsulation Boundary
The domain layer (`Fabolus.Core`) only ever interacts with the managed interface [`IMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IMesh.cs):

```csharp
public interface IMesh
{
    Vector3[] Vertices { get; }
    int[] Triangles { get; }
    MeshMetadata Metadata { get; }
    int VertexCount { get; }
    int TriangleCount { get; }
    bool IsEmpty { get; }
    IMesh WithMetadata(MeshMetadata metadata);
}
```

The concrete class [`MRMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/MRMesh.cs) is marked `internal sealed`. Native pointers (`MR.Mesh*`) never leak across project references.

### 2. Deterministic Disposal with `using` Scopes
Whenever a geometric algorithm is invoked, managed vertex and triangle arrays are marshaled into native C++ structures, computed, and converted back into managed arrays. All unmanaged representations implement `IDisposable` and are enclosed in `using` scopes:

```csharp
public Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0.0f)
{
    try
    {
        using var model = input.ToMRMesh();
        using var mp = new MR.MeshPart(model);
        using var parms = new MR.OffsetParameters()
        {
            voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
        };

        using var result = MR.offsetMesh(mp, offsetDistance, parms);
        return Result.Success(result.ToIMesh(newMetadata));
    }
    catch (Exception ex)
    {
        return new Error("Geometry.OffsetFailed", ex.ToString());
    }
}
```

When the method scope exits, C++ destructors immediately free native heap memory.

---

## Algorithmic Subsystems Deep Dive

### 1. Robust Constructive Solid Geometry (CSG Booleans) ([`Booleans.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/Booleans.cs))
- **Difference (`Subtract`)**: Used for cavity coring and vent drilling.
- **Union (`Union`)**: Merging components into contiguous watertight bodies.
- **Intersection (`Intersect`)**: Volume overlap evaluation and planar half-space cutting.
- MeshLib's boolean kernel employs exact arithmetic predicates and adaptive octree spatial partitioning to resolve coplanar facets and near-coincident boundaries without non-manifold crashes.

### 2. Morphological Offsetting ([`GeometryModifiers.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryModifiers.cs))
- Implements continuous Signed Distance Field (SDF) offsetting:
  - Outward Offset: $\mathcal{M} \oplus d$
  - Inward Offset: $\mathcal{M} \ominus d$
  - Double Offset: $(\mathcal{M} \oplus d) \ominus d$
- By rasterizing the boundary into an adaptive voxel field, narrow grooves (voxel stepping) collapse, while continuous outer bounds are preserved.

### 3. Swept 3D Tubes via Parallel Transport (Bishop Frame) ([`GeometryGenerators.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryGenerators.cs#L18))
When sweeping a 3D cylinder or cone along an arbitrary 3D curve (such as an Angled air channel), classic Frenet-Serret framing produces catastrophic gimbal twists at inflection points where curvature approaches zero ($\kappa \to 0$).

<!-- IMAGE_PLACEHOLDER: [Figure 12.2: Parallel Transport Frame vs Frenet-Serret Frame. Mathematical diagram illustrating gimbal twist in Frenet frames along inflections vs stable Bishop frame transport maintaining smooth radial orientation. Dimensions: 800x400px.] -->

Fabolus implements a **Parallel Transport (Bishop) Frame**:
1. At path point $\mathbf{p}_0$, compute an initial orthogonal basis $(\mathbf{U}_0, \mathbf{W}_0)$ perpendicular to tangent $\mathbf{T}_0$.
2. For each subsequent point $\mathbf{p}_i$:
   - Compute tangent vector: $\mathbf{T}_i = \frac{\mathbf{p}_{i+1} - \mathbf{p}_i}{\|\mathbf{p}_{i+1} - \mathbf{p}_i\|}$
   - Compute rotation axis: $\mathbf{a} = \mathbf{T}_{i-1} \times \mathbf{T}_i$
   - Compute rotation angle: $\theta = \arccos(\mathbf{T}_{i-1} \cdot \mathbf{T}_i)$
   - Form the rotation quaternion:
     $$\mathbf{q} = \operatorname{Quaternion}\left(\frac{\mathbf{a}}{\|\mathbf{a}\|}, \; \theta\right)$$
   - Transport the basis vectors: $\mathbf{U}_i = \mathbf{q} \mathbf{U}_{i-1} \mathbf{q}^{-1}$, $\mathbf{W}_i = \mathbf{q} \mathbf{W}_{i-1} \mathbf{q}^{-1}$.
3. Generate ring vertices at radius $R_i$ around $(\mathbf{U}_i, \mathbf{W}_i)$ and stitch quad-strip triangles between adjacent rings.

### 4. 2D Silhouette Offsetting via Clipper2
To generate convex hull and shadow projections for sacrificial moulds:
- Vertices are projected onto the $XY$ plane.
- The 2D boundary polygon is offset by `OffsetXY` using `Clipper2Lib` with smooth circular arc joins (`JoinType.Round`).
- The expanded 2D contours are extruded vertically with ear-clipping polygon triangulation for the bottom and top end-caps.
