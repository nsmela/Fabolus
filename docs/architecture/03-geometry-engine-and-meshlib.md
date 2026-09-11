# Geometry Engine & Native MeshLib

## The 3D Calculation Engine

Fabolus delegates heavy 3D calculations to **MeshLib**, a fast and robust computational geometry library written in C++ by MeshInspector.

MeshLib is connected to Fabolus through official .NET interop bindings and wrapped entirely within the `Geometry.MeshLib` project. This design isolates all native C++ code in one place, keeping the rest of the application written in clean, safe C#.

<!-- IMAGE_PLACEHOLDER: [Figure 12.1: Managed C# to Native C++ Marshaling Lifecycle. Memory layout diagram contrasting managed heap arrays with native unmanaged C++ heap structs and deterministic disposal boundaries. Dimensions: 900x450px.] -->

---

## Safe Memory Management

Running fast native C++ code inside a C# (.NET) application can easily cause memory leaks or crashes if not handled carefully. Fabolus prevents this using two strict rules:

### 1. Clear Separation Between C# and C++
The main application (`Fabolus.Core` and `Fabolus.Wpf`) only ever works with standard, managed C# meshes ([`IMesh`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Core/Geometry/IMesh.cs)):

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

The underlying C++ wrapper class (`MRMesh`) is strictly internal. Raw C++ memory pointers are never exposed to the rest of the program.

### 2. Immediate Memory Cleanup
When an operation runs, Fabolus temporarily copies the 3D data into C++, performs the calculation, and copies the resulting shape back into C#.

All C++ resources implement `IDisposable` and are enclosed in `using` statements. As soon as the calculation finishes, the temporary C++ memory is immediately freed:

```csharp
public Result<IMesh> Offset(IMesh input, float offsetDistance, float cellSize = 0.0f)
{
    try
    {
        // 1. Temporarily pass mesh to C++
        using var model = input.ToMRMesh();
        using var mp = new MR.MeshPart(model);
        using var parms = new MR.OffsetParameters()
        {
            voxelSize = cellSize > 0 ? cellSize : MR.suggestVoxelSize(mp, 1e6f),
        };

        // 2. Run fast native algorithm
        using var result = MR.offsetMesh(mp, offsetDistance, parms);

        // 3. Return clean C# mesh; C++ objects are disposed automatically
        return Result.Success(result.ToIMesh(newMetadata));
    }
    catch (Exception ex)
    {
        return new Error("Geometry.OffsetFailed", ex.ToString());
    }
}
```

This guarantees high calculation speeds while completely preventing memory leaks, even during long design sessions with large files.

---

## Key 3D Algorithms

### 1. Solid 3D Operations (CSG Booleans) ([`Booleans.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Geometry.MeshLib/Booleans.cs))
Boolean operations combine or subtract 3D shapes:
- **Subtract**: Carves the bolus cavity and air channel tunnels out of the solid mould block.
- **Union**: Merges separate 3D bodies into a single continuous, watertight solid.
- **Intersect**: Evaluates overlapping areas or trims meshes along a cutting plane.

MeshLib's boolean engine handles complex medical meshes reliably, avoiding the crashes and surface inversion common in basic CAD tools.

### 2. Volume-Preserving Smoothing ([`GeometryModifiers.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Geometry.MeshLib/GeometryModifiers.cs))
Instead of simple surface blurring (which shrinks the model and thins bolus walls), Fabolus uses a **two-step offset**:
1. **Inflate**: Expands the surface outward by a set distance, filling the sharp stair-stepping gaps between CT slices.
2. **Deflate**: Contracts the surface back inward by the exact same distance.

This removes sharp ridges and corners while returning flat and broad regions to their planned thickness, ensuring the prescribed radiation dose is delivered accurately.

### 3. Smooth Curved Air Channels ([`GeometryGenerators.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Geometry.MeshLib/GeometryGenerators.cs#L18))
When generating curved or angled air channels, Fabolus sweeps circular rings along the channel's 3D path and connects them with triangle strips:
- Standard curve-following techniques often twist or pinch when a curve turns or flattens out.
- Fabolus uses a stable orientation technique (known as a **Parallel Transport** or **Bishop frame**) to keep each circular cross-section aligned smoothly with the path.
- This produces clean, un-twisted cylindrical tubes that ensure silicone can enter and air can escape without obstruction.

<!-- IMAGE_PLACEHOLDER: [Figure 12.2: Parallel Transport Frame vs Standard Framing. Visual comparison showing how stable frame transport prevents twisting along curved 3D channel paths. Dimensions: 800x400px.] -->

### 4. 2D Mould Footprints via Clipper2
To create mould shells (Convex, Concave, or Contoured):
- Fabolus projects the bottom outline of the bolus downward onto a flat 2D plane.
- The 2D outline is expanded outward by the specified wall thickness using the **Clipper2** polygon library, with smooth rounded corners.
- The expanded 2D shape is then extruded vertically to create the mould walls, capped with flat top and bottom faces.
