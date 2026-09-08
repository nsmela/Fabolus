# Mesh Inspection & Repair

Before a mesh can be smoothed or turned into a mould, it needs to be a clean, closed solid. Fabolus inspects every imported mesh and can repair common faults.

---

## Watertight and manifold

Downstream mould generation and slicing rely on the mesh being:

- **Watertight**: closed, with no boundary (open) edges, to know without a doubt what is "inside" (solid silicone) and what is "outside" (air) of the mesh.
- **Manifold**: every edge is shared by exactly two faces, with no surfaces passing through each other or joining along a single edge.

When converting stacked 2D CT contours into a 3D STL file, mesh errors can occur:

| Common Defect | What Happened? | What Happens If Not Fixed? |
| :--- | :--- | :--- |
| **Open Boundaries (Holes)** | Missing triangles or gaps between CT slices leave the surface open. | The software cannot tell inside from outside. Mould carving or slicing may fail. |
| **Non-Manifold Edges** | Faces intersect at sharp angles or three or more faces share a single edge. | Downstream boolean operations and smoothing can produce errors or inverted surfaces. |
| **Self-Intersections** | Surfaces fold through themselves. | Can cause micro-voids, internal cavities, or failed mould subtractions. |
| **Degenerate Triangles** | Near-zero-area or collapsed sliver triangles. | Can stall geometric calculations during smoothing or slicing. |
| **Multiple Meshes** | The file contains two or more separate, disconnected parts. | Mould generation may envelope unwanted geometry or fail to generate a clean cavity. |
| **Meshes with Tiny Islands** | Stray CT noise or tiny floating mesh fragments disconnected from the main body. | Floating fragments cause boolean failures or create unwanted voids/debris in the printed mould. |

<!-- IMAGE_PLACEHOLDER: [Figure 3.1: Topological defects — open holes, non-manifold edges, self-intersections, degenerate triangles, and tiny floating islands.] -->

---

## The Info Panel

When a mesh is active, the Info Panel on the right reports its geometry. The values come directly from Fabolus's evaluation of the mesh:

### Mesh statistics

- **Triangles** — triangle count.
- **Surface Area** — in mm².
- **Volume** — in mL.
- **Dimensions** — bounding box as width × height × depth in mm.

### Mesh topology

Each status is shown in green for good, or red if an issue is detected:

- **Manifold**:
  - :white_check_mark: **Green (`Yes`)**: Continuous, valid surface.
  - :x: **Red (`No`)**: Non-manifold edges detected. Repair is recommended.
- **WaterTight**:
  - :white_check_mark: **Green (`Yes`)**: Fully closed solid.
  - :x: **Red (`No`)**: Holes were found. Repair is recommended.
- **Orphaned Vertices**:
  - :white_check_mark: **Green (`No`)**: No unused vertices.
  - :x: **Red (`Yes`)**: Stray vertices detected. Repair is recommended.
- **Degenerate Triangles**:
  - :white_check_mark: **Green (`No`)**: No zero-area triangles.
  - :x: **Red (`Yes`)**: Degenerate triangles detected. Repair is recommended.
- **Is Self-Intersecting**:
  - :white_check_mark: **Green (`No`)**: Clean geometry.
  - :x: **Red (`Yes`)**: Self-intersecting triangles detected. Repair is recommended.

<!-- IMAGE_PLACEHOLDER: [Figure 3.2: Info panel showing mesh statistics and topology status for an imported bolus.] -->

---

## Mesh Repair

1. Select the mesh in the list on the left.
2. Click **Repair Mesh**.
3. Fabolus repairs the mesh (there is also an option to fix self-intersections), then re-evaluates its topology and statistics.

Repair updates the selected mesh **in place** — it keeps the same entry rather than adding a separate copy. After repair, re-check the Info Panel to confirm the topology status.

<!-- IMAGE_PLACEHOLDER: [Figure 3.3: A mesh before and after repair, with the topology status updating from red to green.] -->
