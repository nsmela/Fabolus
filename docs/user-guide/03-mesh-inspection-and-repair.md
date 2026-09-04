# Mesh Inspection & One-Click Repair

Before creating a casting mould, your 3D bolus file must be a clean, solid 3D object. In 3D printing, this is known as being **watertight** and **manifold**.

---

## What Makes a 3D Model Print-Ready?

Think of a 3D model like a sealed water balloon:

1. **Watertight (Solid with No Holes)**:
   - There are no gaps, cracks, or missing faces. If you filled the shape with water, not a single drop would leak out.
   - *Why it matters*: 3D slicer software and mould carving tools need to know without a doubt what is "inside" (solid silicone) and what is "outside" (air). If there are holes, the slicer can get confused and fail to print the walls.
2. **Manifold (Clean Surface Topology)**:
   - The outer skin of the 3D model is continuous and doesn't pinch or intersect itself.
   - *Why it matters*: If two surfaces pass through each other like ghosts, or three walls join at a single razor-thin edge (like an open book), the 3D printer won't know which side to fill, causing printing defects and silicone leaks.

<!-- IMAGE_PLACEHOLDER: [Figure 3.1: Topological Defects Explained. Four simple diagrams illustrating: (A) Open Hole where a triangle is missing, (B) T-Junction where multiple walls touch an edge, (C) Bowtie Vertex where two shapes touch at a single point, (D) Self-Intersecting Faces crossing through each other. Dimensions: 900x400px.] -->

---

## Why Hospital CT Exports Have Errors

When a doctor or dosimetrist contours a bolus in a Treatment Planning System (such as Eclipse, RayStation, or Monaco), the contours are drawn on individual 2D CT slices. When the software converts these stacked 2D lines into a 3D STL file, common glitches occur:

| Common Defect | What Happened? | What Happens If Not Fixed? |
| :--- | :--- | :--- |
| **Open Holes (Boundary Edges)** | The contouring stopped on the top or bottom CT slice without a closing cap. | The mould carver cannot tell inside from outside. The 3D printer slicer may treat the model as a paper-thin shell. |
| **Non-Manifold Edges** | Two contour loops touched along a single line, joining three or more walls together. | Slicers fail to slice layers cleanly, causing extruder clogs or air-printing gaps. |
| **Self-Intersections** | Surfaces fold backward and pass through each other. | Creates hidden cavities inside the plastic that trap air and cause silicone leaks. |
| **Tiny Sliver Triangles** | Extremely thin, needle-like triangles created during export. | Can cause 3D software to freeze or calculate surface normals incorrectly. |

---

## Checking Your Model in Fabolus

When you open a file in Fabolus, the application automatically inspects the geometry and displays its health in the **Info Panel** on the right side of the screen:

<!-- IMAGE_PLACEHOLDER: [Figure 3.2: Fabolus Mesh Items List and Topology Alert Interface. Screenshot showing the left drawer displaying imported meshes ('scalp_bolus.stl', 'ear_bolus.stl'), active selection badges, physical dimensions, and topology diagnostic indicators with red alerts on non-manifold edges. Dimensions: 900x600px.] -->

### What to Look For in the Info Panel:
- **Dimensions ($W \times D \times H$)**: Confirms that your bolus imported in millimeters ($mm$) and matches the patient's anatomy (e.g., an ear bolus shouldn't be $500\text{ mm}$ wide!).
- **Volume**: The exact volume in cubic centimeters ($cm^3$) or milliliters ($mL$). You will use this number later to measure out your liquid silicone.
- **Watertight Status**:
  - :white_check_mark: **Green (`Yes`)**: The model is completely sealed. You are ready to smooth and mould.
  - :x: **Red (`No`)**: Holes were found. You must run repair before proceeding.
- **Manifold Status**:
  - :white_check_mark: **Green (`Yes`)**: Clean surface.
  - :x: **Red (`No`)**: Non-manifold edges detected. Repair is required.
- **Self-Intersections**:
  - :white_check_mark: **Green (`0`)**: Perfectly clean geometry.
  - :warning: **Yellow / Red (`> 0`)**: Overlapping surfaces found. Recommended to repair.

---

## One-Click Mesh Repair

If Fabolus detects holes or non-manifold edges, fixing them takes just one click:

1. Select the defective mesh in the **Mesh Items** list on the left.
2. Click the **Repair Mesh** button in the left tool panel.
3. Fabolus automatically:
   - Patches all open holes with sealed caps.
   - Untangles and separates non-manifold edges.
   - Removes zero-area sliver triangles.
4. A clean, repaired model is added to your workspace with the name `{OriginalName} (Repaired)`.
5. All status badges switch to **Green**, confirming that your bolus is solid and ready for smoothing!

<!-- IMAGE_PLACEHOLDER: [Figure 3.3: Mesh Repair Before-and-After Comparison. Close-up 3D view showing an open, unsealed CT end-cap and self-intersecting facets cleanly patched and manifold after clicking Repair Mesh. Dimensions: 900x450px.] -->
