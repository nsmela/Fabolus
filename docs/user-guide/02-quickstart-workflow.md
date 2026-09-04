# Quickstart: 15-Minute Bolus Workflow

This guide walks through the complete end-to-end workflow in Fabolus, from loading an exported DICOM/STL contour to generating a cast-ready sacrificial mould package.

---

## Step 1: Import the Bolus Mesh
1. Launch **Fabolus**.
2. Click the **meshes** tab in the top navigation bar.
3. Click **Import Mesh** (or press `Ctrl+O`) and select your bolus file (`.stl`, `.3mf`, `.obj`).
4. Once loaded, the mesh appears in the 3D viewport. The right-hand panel displays physical statistics:
   - Volume ($cm^3$ or $mL$)
   - Surface Area ($cm^2$)
   - Dimensions ($W \times D \times H$ in $mm$)
   - Watertightness and manifold status

> [!NOTE]
> If the mesh contains open boundary edges or non-manifold edges, the **Topology Alert** will highlight them in red. Click **Repair Mesh** to execute an automatic repair.

---

## Step 2: Apply Volume-Preserving Smoothing
1. Click the **smooth** tab in the top navigation bar.
2. Adjust the smoothing controls:
   - **Intensity ($mm$)**: Controls the erosion/dilation distance (default `1.0–1.5 mm`).
   - **Iterations**: Number of morphological passes (default `1`).
   - **Remesh Ratio**: Density multiplier for post-smoothing triangle decimation (default `1.0`).
3. Click **Apply Smoothing**.
4. Verify surface deviation:
   - Toggle **Heatmap** to inspect the signed distance deviation between the raw CT surface and smoothed mesh.
   - Toggle **Cutting Plane** and drag the 3D height manipulator to inspect the 2D cross-sectional thickness.
   - Ensure the volume change reported in the **Info Panel** remains within your clinic's tolerance (typically $\pm 1\%$).

---

## Step 3: Optimize Print Orientation & Overhangs
1. Click the **rotate** tab in the top navigation bar.
2. Observe the overhang gradient:
   - **Green**: Support-free print angles.
   - **Yellow**: Angles approaching the warning threshold (default 45°).
   - **Red**: Severe overhangs exceeding the critical threshold (default 65°) requiring supports.
3. Use the 3D rotation gizmo or axis angle sliders to orient the mesh so the inner skin-contact surface faces upward (support-free), minimizing print artifact scarring against the patient.

---

## Step 4: Configure the Sacrificial Mould
1. Click the **mould** tab in the top navigation bar.
2. Select your desired mould geometry:
   - **Convex Hull**: Robust rectangular block for flat/chest wall sites.
   - **Concave Shadow**: Tight silhouette mould to reduce resin/filament usage.
   - **Contoured**: Uniform shell offset for large complex anatomy.
3. Set your clearance margins:
   - **Offset XY**: Lateral clearance between bolus and mould wall (default `2.0 mm`).
   - **Offset Bottom / Top**: Vertical mould base and lid thickness (default `2.0 mm`).

---

## Step 5: Place Air Channels & Vents
With the mould tab active, add injection and venting channels:
1. **Injection Sprue (Straight)**:
   - Select **Straight**.
   - Hover the cursor over the lowest point of the bolus and left-click to drop a vertical sprue.
2. **Degassing Vents (Angled / Painted)**:
   - Select **Angled** for side evacuation ports. Click anatomical high points where air could be trapped during upward fluid rise.
   - Select **Painted** to drag a continuous path along curved ridges (e.g. ear rim or nasal bridge).
3. Inspect the live transparent preview in the 3D viewport. Click any channel marker to edit its tip diameter, tube diameter, or penetration depth.

---

## Step 6: Generate the Mould & Export
1. Click **Generate Mould**.
   - Fabolus will construct the outer mould volume, subtract the bolus cavity via boolean CSG, and subtract all air channels.
2. Once generated, inspect the transparent mould cavity in the viewport.
3. Click the **export** tab in the top navigation bar.
4. Choose your export format:
   - **3MF Package (Recommended)**: Packages the print-ready mould, the original base mesh, and the serialized parametric recipe into a single file.
   - **STL**: Standard polygon mesh ready for any 3D slicer (Bambu Studio, PrusaSlicer, Cura, Chitubox).
5. Click **Export Package** to save your file.
