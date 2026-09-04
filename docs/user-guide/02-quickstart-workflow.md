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

## Step 1: Import & Diagnostic Triage

1. Launch **Fabolus**. The application initializes in the **meshes** view.
2. Click **Import Mesh** (or drag and drop your STL file directly into the viewport).
3. The model loads into the 3D scene and populates the **Mesh Items** list on the left.
4. Review the **Physical Statistics Panel** on the right:
   - **Bounding Envelope**: Check that dimensions ($W \times D \times H$) match expected clinical anatomy.
   - **Volume**: Note the initial volume ($V_{\text{initial}}$ in $cm^3$ or $mL$).
   - **Topology Indicators**: Ensure **Watertight** is `Yes` and **Manifold** is `Yes`.
5. *If any topology flag is highlighted in Red*: Click **Repair Mesh** in the left drawer. Fabolus will execute automated hole-closing and non-manifold edge decoupling, generating a clean `(Repaired)` mesh.

<!-- IMAGE_PLACEHOLDER: [Figure 2.2: Mesh Import and Topology Inspection Step. Screenshot showing a loaded patient bolus ('chin_bolus.stl') in the 3D viewport, with the physical statistics drawer displaying volume, surface area, and manifold indicators. Dimensions: 1000x600px.] -->

---

## Step 2: Volume-Preserving Smoothing & QA

1. Click the **smooth** tab in the top navigation bar.
2. Configure the morphological parameters:
   - **Intensity**: Set between `1.0 mm` and `1.5 mm` (roughly matching your CT slice thickness).
   - **Iterations**: Leave at `1` for standard smoothing.
   - **Remesh Ratio**: Set to `1.0` (preserves triangle resolution) or `1.5` (denser, smoother curves).
3. Click **Apply Smoothing**. The background computation completes, updating the 3D scene.
4. **Clinical QA Verification**:
   - Check the **Volume Change** percentage in the Info Panel. It should be strictly within clinical prescription tolerances (typically $\le \pm 1.0\%$).
   - Toggle **Heatmap**: Green indicates zero deviation ($\pm 0.1\text{ mm}$); blue highlights where voxel valleys were filled; red highlights where staircase peaks were rounded.
   - Toggle **Cutting Plane**: Use the vertical manipulator handle to slice through the bolus, directly verifying cross-sectional thickness.

<!-- IMAGE_PLACEHOLDER: [Figure 2.3: Smoothing and Heatmap Deviation Step. Screenshot showing the smoothed bolus with active signed distance heatmap gradient, with the 3D cutting plane activated showing internal wall contours. Dimensions: 1000x600px.] -->

---

## Step 3: Print Orientation & Overhang Optimization

1. Click the **rotate** tab in the top navigation bar.
2. The bolus is immediately colored with an overhang gradient relative to the build plate ($-\hat{z}$ direction):
   - **Green**: Self-supporting surfaces ($0^\circ – 45^\circ$).
   - **Yellow**: Angles approaching warning limit ($45^\circ – 65^\circ$).
   - **Red**: Steep overhangs requiring dense supports ($> 65^\circ$).
3. **The Clinical Goal**: Orient the mesh so that the **skin-contact surface faces UPWARDS**.
4. Drag the 3D rotation gizmo rings or adjust the $X, Y, Z$ degree sliders until the critical patient-facing cavity is entirely green/yellow. Any required print supports will now attach exclusively to the sacrificial outer shell, ensuring a mirror-smooth silicone casting surface.

<!-- IMAGE_PLACEHOLDER: [Figure 2.4: 3D Rotation Gizmo and Overhang Gradient. Screenshot showing active rotation gizmo around the bolus with real-time green/yellow/red normal angle coloring. Dimensions: 1000x600px.] -->

---

## Step 4: Configure Sacrificial Mould Shell

1. Click the **mould** tab in the top navigation bar.
2. Choose your mould geometry:
   - **Convex Hull**: Clean rectangular footprint; easiest to print and clamp (best for chest wall, forehead, or chin).
   - **Concave Shadow**: Tight silhouette offset; saves 30% print material (best for shoulder, clavicle, or asymmetric wraps).
   - **Contoured**: Omnidirectional 3D shell offset (best for large cranial helmets).
3. Adjust clearance offsets:
   - **Offset XY**: Set lateral wall margin to `2.0 mm`.
   - **Offset Bottom / Top**: Set base plate and top cap margin to `2.0 mm` or `3.0 mm`.
4. A semi-transparent cyan mould shell displays in the viewport, confirming adequate enclosing margins.

<!-- IMAGE_PLACEHOLDER: [Figure 2.5: Mould Configuration and Channel Placement Step. Screenshot showing transparent preview mould surrounding the bolus, with an injection sprue at the base and multiple degassing vents placed at anatomical high points. Dimensions: 1000x600px.] -->

---

## Step 5: Place Injection Sprues & Air Channels

While still in the **mould** view, place channels to facilitate bubble-free silicone injection:

1. **Place the Bottom Injection Sprue**:
   - Under Channel Type, select **Straight**.
   - Set Tip Diameter to `2.0 mm`, Channel Diameter to `5.0 mm`.
   - Hover your mouse over the **lowest anatomical point ($Z_{\min}$)** of the bolus and left-click. A vertical injection channel snaps into position.
2. **Place Degassing Vents (Risers)**:
   - For side slopes, select **Angled**. Hover and click each local anatomical crest; Fabolus automatically angles the tip along the surface normal and curves it smoothly upward.
   - For curved anatomical ridges (e.g. ear rim or nose bridge), select **Painted**. Click and drag along the crest to sweep a continuous venting channel.
3. Verify channel clearances in the transparent preview. Click any channel marker to fine-tune its diameter or delete it if misplaced.

---

## Step 6: Bake & Export

1. Click **Generate Mould**. Fabolus executes the boolean CSG pipeline:
   $$\text{Mould} = \text{MouldShell} \setminus \text{Bolus} \setminus \sum \text{Channels}$$
2. The viewport renders the final, hollowed sacrificial mould with internal cavity and vent tunnels.
3. Click the **export** tab in the top navigation bar.
4. Review the **Baked Operations** list:
   - Verified Smoothing intensity and iteration count.
   - Applied rotation quaternion.
   - Mould type and included air channels.
5. Choose **3MF Package** (preserves base mesh and recipe) or **STL**.
6. Set the output file path and click **Export Package**.

<!-- IMAGE_PLACEHOLDER: [Figure 2.6: Export View and 3MF Package Summary. Screenshot showing the export summary panel listing all baked operations, file format toggle, destination folder picker, and the finished printable mould in the viewport. Dimensions: 1000x600px.] -->

Your sacrificial mould file is now ready for your 3D printer slicer!
