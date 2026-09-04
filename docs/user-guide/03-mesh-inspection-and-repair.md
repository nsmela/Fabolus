# Mesh Inspection & Automated Repair

## The Mathematical Importance of 2-Manifold Watertight Solids

In computational solid geometry (CSG) and additive manufacturing, a 3D surface mesh must define a strict, unambiguous volumetric boundary. Mathematically, the mesh must represent a **closed 2-manifold surface** embedded in $\mathbb{R}^3$:

1. **Local Disk Topology**: Every point on the surface has a local neighborhood homeomorphic to an open two-dimensional disk.
2. **Edge Adjacency**: Every edge $e$ in the mesh connects exactly two vertices and is shared by **exactly two** triangular faces with opposing half-edge orientations.
3. **Vertex Manifoldness**: The collection of faces sharing a single vertex forms a single continuous topological fan or cone, with no self-pinches ("bowties").
4. **The Euler-Poincaré Formula**: For a watertight manifold closed genus-$g$ surface:
   $$\chi = V - E + F = 2(1 - g)$$
   *(where $V$ is vertex count, $E$ is edge count, $F$ is face count, and $g$ is the topological genus/number of through-holes).*

<!-- IMAGE_PLACEHOLDER: [Figure 3.1: Topological Defects Explained. Four technical diagrams illustrating: (A) Open Boundary Edge where an edge has only one incident face, (B) Non-Manifold T-Junction Edge shared by 3 or more faces, (C) Non-Manifold Bowtie Vertex connecting two disconnected surface sheets, (D) Self-Intersecting Facets piercing through geometry without shared topological vertices. Dimensions: 900x400px.] -->

---

## Common Defects in Medical TPS Exports

DICOM RT Structure sets are created on 2D axial CT slices. When an automated contouring tool or TPS exports these 2D planar contours to a 3D STL file via marching cubes or Delaunay contour stitching, geometric defects frequently arise:

| Defect Type | Geometric Root Cause | Failure Mode in Slicers & CSG |
| :--- | :--- | :--- |
| **Open Boundaries (Holes)** | End-slices where contouring terminated; missing top/bottom caps | Boolean cavity subtraction cannot identify inside vs. outside. Slicer classifies the object as an infinitely thin hollow shell; infill will fail. |
| **Non-Manifold Edges** | Two contours touching at a single line or facet shared across 3+ triangles | CSG algorithms crash or produce inverted normals. Slicers fail to slice layers cleanly, causing extruder air-printing. |
| **Self-Intersections** | Re-entrant folds and overlapping triangle facets produced during marching cubes | Causes micro-voids, internal cavities, and silicone casting leaks. Slicer path generation oscillates rapidly, creating blobs. |
| **Degenerate Triangles** | Sliver triangles with aspect ratios $> 1000:1$ or area $< 10^{-7}\text{ mm}^2$ | Causes floating-point division by zero during surface normal and cross-product calculations, crashing rendering engines. |

---

## Mesh Management in Fabolus

The **meshes** tab in Fabolus serves as the triage station for every 3D model entering the workspace.

<!-- IMAGE_PLACEHOLDER: [Figure 3.2: Fabolus Mesh Items List and Topology Alert Interface. Screenshot showing the left drawer displaying imported meshes ('scalp_bolus.stl', 'ear_bolus.stl'), active selection badges, physical dimensions, and topology diagnostic indicators with red alerts on non-manifold edges. Dimensions: 900x600px.] -->

### Supported File Formats
- **Import Formats**: `.stl` (Binary and ASCII), `.3mf` (3D Manufacturing Format), `.obj` (Wavefront), `.off` (Object File Format), `.ply` (Polygon File Format).
- **Export Formats**: `.3mf` (complete non-destructive project package), `.stl` (standard tessellation solid).

### Physical Statistics Panel
Selecting a mesh from the **Mesh Items** list populates the physical statistics calculated in real time by [`GeometryEvaluators`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryEvaluators.cs):
- **Bounding Dimensions**: Real-world dimensions in millimeters ($X \times Y \times Z$).
- **True Volume**: Computed using the continuous divergence theorem:
  $$V = \frac{1}{6} \sum_{i=1}^{F} \mathbf{v}_{i,1} \cdot (\mathbf{v}_{i,2} \times \mathbf{v}_{i,3})$$
  *(Reported in cubic centimeters $cm^3$ or milliliters $mL$, accurate to 2 decimal places).*
- **Surface Area**: Total sum of all triangular face areas in square centimeters ($cm^2$).
- **Mesh Complexity**: Total vertex count and triangle count.

---

## Diagnostic Flags & Severity Triage

Fabolus automatically evaluates topological integrity using native MeshLib kernel routines:

```csharp
// Evaluated internally during topology triage:
selfInts = (int)MR.SelfIntersections.getFaces(mlMesh).count();
multipleEdges = MR.findMultipleEdges(mlMesh.topology, null);
bdEdges = MR.findAllLeftBdEdges(mlMesh.topology, null, null);
```

| Indicator | Status | Status Code | Required Clinical Action |
| :--- | :--- | :--- | :--- |
| **Watertight** | Green (`Yes`) | `bdEdges == 0` | Fully closed solid; proceed to smoothing. |
| | Red (`No`) | `bdEdges > 0` | Open holes detected; **Repair Mesh required**. |
| **Manifold** | Green (`Yes`) | `multipleEdges == 0` | Topological disk everywhere; proceed. |
| | Red (`No`) | `multipleEdges > 0` | Non-manifold edges detected; **Repair Mesh required**. |
| **Self-Intersections** | Green (`0`) | `selfInts == 0` | Clean geometry; ready for CSG booleans. |
| | Yellow (`> 0`) | `selfInts > 0` | Minor self-intersections; recommended to auto-repair. |

---

## Step-by-Step: Executing Automated Mesh Repair

When an imported mesh displays one or more red diagnostic flags:

<!-- IMAGE_PLACEHOLDER: [Figure 3.3: Before and After Automatic Mesh Repair. 3D high-resolution wireframe render comparing a defective larynx bolus mesh with missing end-caps and non-manifold T-edges against the automatically repaired, watertight manifold solid. Dimensions: 1000x500px.] -->

1. Ensure the defective mesh is highlighted in the **Mesh Items** list.
2. Click the **Repair Mesh** button in the left tool sidebar.
3. Fabolus invokes [`RepairMesh.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/MeshIO/RepairMesh.cs) through the native MeshLib engine:
   - **Boundary Loop Closure**: Automatically traces open edge loops and generates minimal-energy triangulated patches to seal holes.
   - **Non-Manifold Splitting**: Disconnects overlapping face sheets sharing a single edge and duplicates pinch vertices.
   - **Degenerate Face Removal**: Collapses edges with near-zero length and removes zero-area sliver triangles.
   - **Normal Unification**: Reorients all face normals to point consistently outward based on raycasted ray-parity tests.
4. The healed mesh is appended to the workspace with the name `{OriginalName} (Repaired)` and automatically selected as the active model.
5. All diagnostic flags switch to **Green**, confirming that the bolus is ready for volume-preserving smoothing and boolean mould operations.
