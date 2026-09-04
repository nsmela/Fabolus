# Quickstart Workflow

This walks through the Fabolus workflow end to end: from a bolus mesh exported from a Treatment Planning System (TPS) to an exported, print-ready mould file. Printing and casting happen in other tools and are out of scope here.

---

## Before you start

Fabolus imports mesh files (e.g. `.stl`). It reads the geometry as-is, so the exported mesh should be in millimetres and should cover the intended bolus region.

<!-- IMAGE_PLACEHOLDER: [Figure 2.1: Fabolus main window — step navigation header (meshes, smooth, rotate, decals, mould, export, cut / split), left parameter panel, 3D viewport, and right info panel.] -->

### Viewport camera controls
- **Orbit / rotate**: hold the right mouse button and drag.
- **Pan**: hold the middle mouse button (or `Shift` + right mouse button) and drag.
- **Zoom**: scroll wheel.

---

## Step 1: Import & inspect

1. Launch Fabolus. It opens on the **meshes** view.
2. Click **Import** (or drag an STL file into the window).
3. The mesh appears in the viewport and is listed on the left.
4. Read the **Info Panel** on the right. It reports mesh statistics (triangles, surface area, volume, dimensions) and topology status (Manifold, WaterTight, Orphaned Vertices, Degenerate Triangles, Is Self-Intersecting), each shown green or red.
5. If topology shows problems, select the mesh and click **Repair Mesh**. Repair updates the selected mesh in place and re-checks its topology and statistics. See [Mesh Inspection & Repair](03-mesh-inspection-and-repair.md).

---

## Step 2: Smooth

1. Click the **smooth** tab.
2. Adjust the smoothing controls (Intensity, Iterations, Inflation, Remesh Ratio, Resolution) and click **Apply Smoothing**.
3. Use the display options to review the result: **Heat Map** shows how far the surface moved, **Cross Section** slices through the mesh, and the ghost/comparison options overlay the pre-smoothing shape.

See [Volume-Preserving Smoothing](04-volume-preserving-smoothing.md).

---

## Step 3: Orient & check overhangs

1. Click the **rotate** tab. The mesh is coloured by surface angle using the current warning/critical thresholds (defaults 45° and 65°, both adjustable).
2. Rotate with the X/Y/Z rings, or type angles into the X/Y/Z fields; **Reset** returns to the original orientation.

See [Print Orientation & Overhangs](05-print-orientation-and-overhangs.md).

---

## Step 4: Generate the mould

1. Click the **mould** tab.
2. Choose a mould shape — **Convex**, **Concave**, or **Contoured** — and set the wall offsets (Offset XY, and for Convex/Concave, Offset Bottom / Offset Top; all default `2.0 mm`).
3. A semi-transparent preview shows the mould around the bolus.

See [Sacrificial Mould Design](06-sacrificial-mould-design.md).

---

## Step 5: Add air channels

While in the **mould** tab, add channels so silicone can enter and air can escape. Choose a channel type — **Straight**, **Angled**, or **Painted** — and click (or drag, for Painted) on the mesh to place it.

See [Air Channels & Degassing](07-air-channels-and-degassing.md).

---

## Step 6: Generate & export

1. Click **Generate Mould**. Fabolus subtracts the bolus cavity and the placed channels from the mould shell. **Clear Mould** removes the generated mould and returns to the bolus.
2. Click the **export** tab.
3. Choose a file format — **STL** or **3MF** (3MF also stores the command history and base mesh) — pick a destination, and export.

The exported file is then taken into your slicer and printer, and cast, outside Fabolus.
