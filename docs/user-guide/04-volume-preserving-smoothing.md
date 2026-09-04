# Volume-Preserving Smoothing

## Why Preserving Thickness is Critical

In radiation therapy, your doctor prescribes a very specific bolus thickness—most commonly **5 mm**, **7 mm**, or **10 mm**. The computer calculates the exact radiation dose based on this number:
- **If the bolus gets too thin**: The skin cancer receives less radiation than intended.
- **If the bolus gets too thick**: The beam is blocked too much, which underdoses deeper tissues.

Most 3D graphics software uses simple "vertex averaging" to smooth bumpy models. But on thin boluses, this shrinks the shape like a melting ice cube—often losing **15% to 25% of the total volume**!

Fabolus was designed specifically for oncology to solve this problem. It rounds off CT slice steps while keeping **over 99% of your prescribed thickness and volume**.

---

## How Fabolus Smooths Without Shrinking

Instead of moving points around and shrinking edges, Fabolus uses a balanced two-step approach:

```
Step 1: Expand Outward (+d)          Step 2: Contract Inward (-d)
Bridge the jagged CT steps           Return to the patient's true skin shape
        ┌──┐                                 ╭──╮
   ┌────┘  └────┐                      ╭─────╯  ╰─────╮
   │ Raw Steps  │                      │ Smooth Solid │
   └────────────┘                      ╰──────────────╯
```

1. **Step 1: Gentle Expansion**: The surface is puffed outward by a small distance (`Intensity`). This fills in the sharp valleys between CT slices.
2. **Step 2: Equal Contraction**: The surface is pulled back inward by the exact same distance. Flat and broad areas return to their original coordinates, while the jagged slice corners are left smoothly rounded.
3. **Safety Guard**: If an intensity setting is accidentally set too high, Fabolus stops before the shape can collapse, keeping your model safe.

<!-- IMAGE_PLACEHOLDER: [Figure 4.1: The Smoothing Control Drawer. Screenshot showing parameter sliders (Intensity, Iterations, Inflation, Remesh Ratio, Resolution) alongside the Apply Smoothing action button and clinical volume delta indicator. Dimensions: 400x600px.] -->

---

## Smoothing Controls & Rules of Thumb

| Setting | Default | Recommended Setting | What It Does & Simple Rule of Thumb |
| :--- | :--- | :--- | :--- |
| **Intensity** | `1.5 mm` | Match your CT slice thickness (`1.0 – 2.0 mm`) | **How strong the smoothing is**. Set this close to your original CT scan slice thickness (e.g., if your CT slices are 1.25 mm apart, set Intensity to 1.2 mm – 1.5 mm). |
| **Iterations** | `1` | `1` (use `2` only for very coarse CT scans) | **Number of smoothing passes**. 1 pass is almost always ideal. 2 passes is only needed for rough 3 mm CT slices. |
| **Inflation** | `0.2 mm` | `0.1 – 0.3 mm` | **Volume fine-tuning**. A tiny microscopic puff to replace the volume lost when shaving down 90-degree corners. |
| **Remesh Ratio** | `1.0` | `1.0` (standard) or `1.5` (extra smooth) | **Surface smoothness**. 1.0 keeps the file lightweight and quick to 3D print; 1.5 creates ultra-smooth surfaces for delicate facial curves. |
| **Resolution** | `1.0 mm` | `1.0 mm` | **Detail level**. 1.0 mm works best for general clinical boluses. |

---

## Three Easy Ways to Quality-Check Your Bolus

Before turning your bolus into a mould, use Fabolus's built-in QA tools to confirm that the thickness and shape are accurate:

<!-- IMAGE_PLACEHOLDER: [Figure 4.2: Signed Distance Heatmap Visualization. 3D render of a nasal bolus with color-mapped surface deviation from -1.5 mm (red) to +1.5 mm (blue), highlighting preserved volume along the dorsal bridge. Dimensions: 1000x550px.] -->

### 1. Check the Color Heatmap
Click **Heatmap** to see how much the surface moved compared to the original CT scan:
- **Green**: Perfect match! The surface changed by less than $\pm 0.1\text{ mm}$.
- **Blue**: Grooves that were filled in to smooth out the staircase steps.
- **Red**: Sharp bumps that were rounded down.
- *Check*: The patient-contact side should be almost solid green, confirming that it will fit the patient's skin with zero air gaps.

<!-- IMAGE_PLACEHOLDER: [Figure 4.3: Ghost Overlay Comparison. Semi-transparent phantom overlay comparing original voxelized CT perimeter against smoothed solid, demonstrating boundary adherence along the inner skin contact face. Dimensions: 900x500px.] -->

### 2. Ghost Mode
Toggle **Ghost Mode** to display a translucent outline of the original raw CT shape directly over your smoothed model. This gives you immediate visual confirmation that critical anatomical edges (like around the eye or nose) did not shift.

<!-- IMAGE_PLACEHOLDER: [Figure 4.4: 3D Cutting Plane and Cross-Sectional Contour View. Slicing view showing inner and outer bolus contours with interactive 3D manipulator and thickness dimension callouts. Dimensions: 1000x550px.] -->

### 3. Cross-Section Cutting Plane
- Click **Cutting Plane** to turn on an interactive slicing tool.
- Drag the slider or 3D handle up and down to cut through the bolus like an X-ray.
- Look directly at the cross-section slice to verify that the wall thickness matches your prescription across the entire treatment area.

