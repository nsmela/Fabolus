# Mesh Inspection & Repair

Before a mesh can be smoothed or turned into a mould, it needs to be a clean, closed solid. Fabolus inspects every imported mesh and can repair common faults.

---

## Watertight and manifold

Downstream mould generation and slicing rely on the mesh being:

- **Watertight**: closed, with no boundary (open) edges, so "inside" and "outside" are unambiguous.
- **Manifold**: every edge is shared by exactly two faces, with no surfaces passing through each other or joining along a single edge.

Meshes exported from a TPS are built from stacked 2D CT contours, and that conversion can leave open caps, non-manifold edges, self-intersections, or degenerate (near-zero-area) triangles.

<!-- IMAGE_PLACEHOLDER: [Figure 3.1: Topological defects — an open hole, a non-manifold edge, a self-intersection, and a degenerate sliver triangle.] -->

---

## The Info Panel

When a mesh is active, the Info Panel on the right reports its geometry. The values come directly from Fabolus's evaluation of the mesh:

**Mesh statistics**

- **Triangles** — triangle count.
- **Surface Area** — in mm².
- **Volume** — in mL.
- **Dimensions** — bounding box as width × height × depth in mm.

**Mesh topology** (each shown green for good, red for a problem)

- **Manifold** — Yes/No; the non-manifold edge count is listed when it is not manifold.
- **WaterTight** — Yes/No.
- **Orphaned Vertices** — Yes/No.
- **Degenerate Triangles** — Yes/No.
- **Is Self-Intersecting** — Yes/No; the self-intersecting triangle count is listed when there are any.

<!-- IMAGE_PLACEHOLDER: [Figure 3.2: Info panel showing mesh statistics and topology status for an imported bolus. ] -->

---

## Repairing a mesh

1. Select the mesh in the list on the left.
2. Click **Repair Mesh**.
3. Fabolus repairs the mesh (there is also an option to fix self-intersections), then re-evaluates its topology and statistics.

Repair updates the selected mesh **in place** — it keeps the same entry rather than adding a separate copy. After repair, re-check the Info Panel to confirm the topology status.

<!-- IMAGE_PLACEHOLDER: [Figure 3.3: A mesh before and after repair, with the topology status updating from red to green.] -->
