# Basic Workflow (Quickstart)

This is the basic workflow that utilizes Fabolus end to end: taking a bolus mesh exported from a Treatment Planning System (TPS) through to an exported, print-ready mould file. Physical 3D printing and casting happen in external software and lab equipment and are out of scope here.

---

## Before you start

Fabolus imports mesh files (e.g. `.stl`). Optionally, it can import `.3mf` files which may have saved prior Fabolus edits and command history. It reads the geometry as-is, so exported meshes should be in millimetres and cover the intended bolus region.

<!-- IMAGE_PLACEHOLDER: [Figure 2.1: Fabolus main window — step navigation header (meshes, smooth, rotate, decals, mould, export, cut / split), left parameter panel, 3D viewport, and right info panel.] -->

### Viewport camera controls
- **Orbit / rotate**: hold the right mouse button and drag.
- **Pan**: hold the middle mouse button (or `Shift` + right mouse button) and drag.
- **Zoom**: scroll wheel.

---

## Step 1: Import & inspect

1. Launch Fabolus. It opens on the **meshes** view.
2. Click **Import** (or drag an STL or 3MF file into the window).
3. The mesh appears in the viewport and is listed on the left.
4. Read the **Info Panel** on the right. It reports mesh statistics (triangles, surface area, volume, dimensions) and topology status (Manifold, WaterTight, Orphaned Vertices, Degenerate Triangles, Is Self-Intersecting), each shown in green or red.
5. If topology shows problems (red indicators), select the mesh and click **Repair Mesh**. Mesh defects may cause issues with the smoothing, mould, cut, split, or export functions. Repair updates the selected mesh in place and re-checks its topology and statistics. See [Mesh Inspection & Repair](03-mesh-inspection-and-repair.md).

---

## Step 2: Smooth

1. Click the **smooth** tab.
2. Adjust the smoothing controls (Intensity, Iterations, Inflation, Remesh Ratio, Resolution) and click **Apply Smoothing**.
3. Use the display options to review the result: **Heat Map** shows how far the surface moved, with red showing the gap between the old mesh and the smoothed version. **Cross Section** slices through the mesh, and the ghost/comparison options overlay the pre-smoothing shape.

See [Volume-Preserving Smoothing](04-volume-preserving-smoothing.md).

---

## Step 3: Orient & check overhangs

1. Click the **rotate** tab. The mesh is coloured by surface angle using the current warning/critical thresholds (defaults 45° and 65°, both adjustable).
2. Rotate with the X/Y/Z rings, or type angles into the X/Y/Z fields; **Reset** returns to the original orientation.

> [!WARNING]
> **Pipeline Invalidation**: Edits to the smoothing and rotation clear all progress on mould and air channels. Ensure smoothing and print orientation are finalized before setting up mould channels.

See [Print Orientation & Overhangs](05-print-orientation-and-overhangs.md).

---

## Step 4: Add decals

1. Click the **decals** tab.
2. By default, Fabolus automatically generates and places text decals: the mesh filename on the front anchor and the bolus volume on the back anchor.
3. Review and adjust decal text, font, cap height, depth, or placement if desired. These markings engrave or emboss identifiers directly into the mould wall so they are transferred to the cast silicone bolus.

<!-- IMAGE_PLACEHOLDER: [Figure 2.2: Decals tab showing automatic filename and volume decals placed on the mesh.] -->

---

## Step 5: Add air channels

Air channels must be added before generating the mould so silicone can enter and trapped air can escape during casting.

1. Click the **mould** tab.
2. Choose a mould shape — **Convex**, **Concave**, or **Contoured** — and set the wall offsets. A semi-transparent preview shows the mould shell around the bolus.
3. Choose a channel type — **Straight**, **Angled**, or **Painted** — and click (or drag, for Painted) on the mesh preview to place it.
4. Adjust channel diameters or tip parameters as needed.

<!-- IMAGE_PLACEHOLDER: [Figure 2.3: Placing air channels on the mesh within the mould preview.] -->

See [Air Channels](06-air-channels-and-degassing.md).

---

## Step 6: Generate the mould

1. With channels placed, click **Generate Mould**. Fabolus subtracts the bolus cavity and all placed air channels from the mould shell, creating the finished sacrificial mould.
2. If adjustments are needed, click **Clear Mould** to remove the generated mould and return to the bolus and channel preview.

<!-- IMAGE_PLACEHOLDER: [Figure 2.4: Generated sacrificial mould showing cavity and channel openings.] -->

See [Sacrificial Mould Design](07-sacrificial-mould-design.md).

---

## Step 7: Export

1. Click the **export** tab.
2. Choose a file format:
   - **STL** — exports the printable mould geometry.
   - **3MF** — stores the command history, base mesh, and project settings alongside the mould geometry for future re-editing.
3. Pick a destination folder and click **Export**.

The exported file is then taken into your slicer and 3D printer, and cast in silicone outside Fabolus.

See [Export](09-slicing-printing-and-casting.md).
