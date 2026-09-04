# Quickstart: 15-Minute Bolus Workflow

This guide walks through the standard 15-minute clinical CAD/CAM workflow in Fabolus, taking a raw DICOM/STL bolus structure exported from your Treatment Planning System (TPS) through to a print-ready sacrificial casting mould.

---

## Prerequisites & Pre-Flight Checklist

Before launching Fabolus, ensure your treatment planning export adheres to clinical standards:
- [ ] **Export Format**: Binary `.stl` or uncompressed `.obj` exported directly from your TPS structure set.
- [ ] **Coordinate System**: Verify that coordinates are in **millimeters** ($mm$).
- [ ] **Anatomical Boundary**: Confirm the contour encompasses the entire prescribed bolus volume without artificial clipping against the CT field-of-view (FOV) border.

<!-- IMAGE_PLACEHOLDER: [Figure 2.1: Fabolus Main Window User Interface Tour. Annotated screenshot of the application layout: (1) Top Tab Bar (meshes, smooth, rotate, mould, export), (2) Left Parameter Panel, (3) 3D DirectX 11 SharpDX Viewport, (4) Top-Right Overlay Buttons (Wireframe, Screenshot, Preferences), (5) Right InfoPanel (volume, dimensions, topology alerts), (6) Bottom Status Bar. Dimensions: 1200x700px.] -->

### Viewport Camera Controls
- **Orbit / Rotate**: Hold **Right Mouse Button** (or Left Mouse Button in selection-free zones) and drag.
- **Pan**: Hold **Middle Mouse Button** (or `Shift` + Right Mouse Button) and drag.
- **Zoom**: Rotate the **Scroll Wheel**.
- **Zoom Extents**: Press `Spacebar` or double-click Middle Mouse Button to center the active model.

---

## Step 1: Import & Health Check

1. Launch **Fabolus**. The application opens to the **meshes** view.
2. Click **Import Mesh** (or drag and drop your STL file directly into the window).
3. The model appears in the 3D viewport and is listed under **Mesh Items** on the left.
4. Check the **Info Panel** on the right:
   - **Dimensions**: Check that size ($W \times D \times H$) matches the patient's anatomy in millimeters.
   - **Volume**: Note your starting volume ($cm^3$ or $mL$) to calculate how much silicone to mix later.
   - **Status Badges**: Make sure **Watertight** and **Manifold** are both **Green (`Yes`)**.
5. *If any badge is Red*: Click **Repair Mesh** in the left panel. Fabolus will automatically fix holes and errors, creating a clean `(Repaired)` model.

<!-- IMAGE_PLACEHOLDER: [Figure 2.2: Mesh Import and Topology Inspection Step. Screenshot showing a loaded patient bolus ('chin_bolus.stl') in the 3D viewport, with the physical statistics drawer displaying volume, surface area, and manifold indicators. Dimensions: 1000x600px.] -->

---

## Step 2: Smooth the CT Slices

1. Click the **smooth** tab in the top navigation bar.
2. Set your smoothing controls:
   - **Intensity**: Set between `1.0 mm` and `1.5 mm` (roughly matching your CT slice thickness).
   - **Iterations**: Leave at `1`.
   - **Remesh Ratio**: Leave at `1.0` (or `1.5` for delicate facial curves).
3. Click **Apply Smoothing**.
4. **Quick QA Check**:
   - Check the **Volume Change** in the Info Panel. It should be less than **$\pm 1.0\%$**.
   - Toggle **Heatmap**: Green means the shape didn't shrink; blue filled the grooves; red smoothed the bumps.
   - Toggle **Cutting Plane**: Drag the slicing handle to inspect wall thickness across the bolus.

<!-- IMAGE_PLACEHOLDER: [Figure 2.3: Smoothing and Heatmap Deviation Step. Screenshot showing the smoothed bolus with active signed distance heatmap gradient, with the 3D cutting plane activated showing internal wall contours. Dimensions: 1000x600px.] -->

---

## Step 3: Rotate & Check Overhangs

1. Click the **rotate** tab in the top navigation bar.
2. The bolus is colored with a traffic light gradient:
   - **Green**: Safe to print without supports ($0^\circ – 45^\circ$).
   - **Yellow**: Moderate slope ($45^\circ – 65^\circ$).
   - **Red**: Steep overhang; will droop without supports ($> 65^\circ$).
3. **The Golden Rule**: Rotate the bolus until the **skin-contact side faces UPWARDS**.
4. Drag the 3D rotation rings until the patient-facing side is green and yellow. Any supports will now print harmlessly on the outside of the disposable mould.

<!-- IMAGE_PLACEHOLDER: [Figure 2.4: 3D Rotation Gizmo and Overhang Gradient. Screenshot showing active rotation gizmo around the bolus with real-time green/yellow/red normal angle coloring. Dimensions: 1000x600px.] -->

---

## Step 4: Choose Your Mould Shape

1. Click the **mould** tab in the top navigation bar.
2. Choose your mould shape:
   - **Convex Hull**: Sturdy box shape; easiest to print and stand on a table (best for chest, forehead, or chin).
   - **Concave Shadow**: Follows the outline closely; saves 30% print time (best for shoulders and curved wraps).
   - **Contoured**: Skin-tight shell (best for large head helmets).
3. Check wall thickness:
   - **Offset XY**: Side wall thickness (default `2.0 mm`).
   - **Offset Bottom / Top**: Floor and ceiling thickness (default `2.0 mm` or `3.0 mm`).
4. A transparent cyan mould shell appears around your bolus in the viewport.

<!-- IMAGE_PLACEHOLDER: [Figure 2.5: Mould Configuration and Channel Placement Step. Screenshot showing transparent preview mould surrounding the bolus, with an injection sprue at the base and multiple degassing vents placed at anatomical high points. Dimensions: 1000x600px.] -->

---

## Step 5: Add Injection Port & Air Vents

While in the **mould** view, place channels so silicone can enter and air can escape:

1. **Add the Bottom Injection Port**:
   - Under Channel Type, select **Straight**.
   - Hover your mouse over the **lowest point** of the bolus and click to place the port.
2. **Add Degassing Air Vents**:
   - For side slopes, select **Angled**. Click each anatomical peak; the vent automatically points outward and curves upward.
   - For curved edges (like an ear rim), select **Painted** and drag your mouse along the ridge.
3. Check that all vents exit through the top of the cyan preview mould.

---

## Step 6: Generate Mould & Export

1. Click **Generate Mould**. Fabolus carves out the hollow bolus cavity and drills all your vent tunnels.
2. Click the **export** tab in the top navigation bar.
3. Choose **3MF Package** (saves the entire project so you can re-open and edit later) or **STL**.
4. Select your destination folder and click **Export Package**.

<!-- IMAGE_PLACEHOLDER: [Figure 2.6: Export View and 3MF Package Summary. Screenshot showing the export summary panel listing all baked operations, file format toggle, destination folder picker, and the finished printable mould in the viewport. Dimensions: 1000x600px.] -->

Your mould is ready to load into your 3D printer slicer!
