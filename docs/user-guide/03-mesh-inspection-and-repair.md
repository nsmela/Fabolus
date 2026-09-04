# Mesh Inspection & Automated Repair

## The Importance of Watertight Geometry

In additive manufacturing and computational solid geometry (CSG), a 3D model must be a **2-manifold, closed (watertight) solid**. A model is watertight if every edge is shared by exactly two triangular faces, with consistent outward-pointing surface normals.

Meshes exported from medical Treatment Planning Systems (TPS) frequently contain defects due to marching cubes discretization, contour interpolation errors, and DICOM-to-STL conversions:
- **Open Boundaries (Holes)**: Edges belonging to only one triangle, leaving an unsealed interior.
- **Non-Manifold Edges**: Edges shared by three or more triangles, creating impossible T-junctions.
- **Self-Intersections**: Interpenetrating triangles that produce undefined interior/exterior spaces during boolean subtractions.
- **Degenerate Triangles**: Zero-area or needle-thin sliver triangles that cause numerical instability in slicers and geometry engines.

If a defective mesh is fed into boolean operations (such as cavity subtraction or air channel coring), the CSG engine will fail, produce invalid geometry, or crash the slicer.

---

## Mesh Management in Fabolus

The **meshes** tab in Fabolus acts as the primary workspace inspector.

### Supported File Formats
- **Import**: `.stl` (Binary and ASCII), `.3mf`, `.obj`, `.off`, `.ply`
- **Export**: `.3mf` (Full parametric container), `.stl`

### Physical Statistics Panel
When a mesh is loaded and active, Fabolus displays real-time geometric statistics computed via `IGeometryEvaluators`:
- **Bounding Dimensions**: Width ($X$), Depth ($Y$), and Height ($Z$) in millimeters.
- **Volume**: Accurate volume in cubic centimeters ($cm^3$) or milliliters ($mL$), calculated via the divergence theorem over closed triangular facets.
- **Surface Area**: Total outer surface area in square centimeters ($cm^2$).
- **Complexity**: Total Vertex Count and Triangle Count.

---

## Topology Diagnostics & Error States

Fabolus automatically evaluates the topological integrity of every mesh entering the workspace using MeshLib's native topology analyzer:

| Diagnostic Indicator | Healthy Value | Problem State | Clinical / Print Impact |
| :--- | :--- | :--- | :--- |
| **Watertight** | `Yes` (0 open edges) | `No` (>0 open edges) | Slicer cannot determine inside vs. outside; infill will fail. |
| **Manifold** | `Yes` (0 non-manifold edges) | `No` (>0 non-manifold edges) | Boolean operations in Moulding will fail. |
| **Self-Intersections** | `0` | `> 0` | Micro-voids inside the silicone cast. |
| **Degenerate Triangles** | `0` | `> 0` | Slicer slicing errors, jagged perimeter artifacts. |

---

## Automated Mesh Repair

When a mesh displays topology defects:
1. Select the defective mesh from the **Mesh Items** list.
2. Click the **Repair Mesh** button in the tools panel.
3. Fabolus runs the automated MeshLib repair pipeline:
   - Identifies and seals open boundary loops using planar or minimum-area hole filling.
   - Decouples non-manifold vertices and splits multi-fan edges.
   - Cleans degenerate triangles and eliminates self-intersecting facets.
   - Recomputes outward-pointing vertex and face normals.
4. The repaired mesh is added to the workspace with the tag `(Repaired)` and automatically set as the active model. Physical statistics and topology flags update immediately to confirm a clean manifold solid.
