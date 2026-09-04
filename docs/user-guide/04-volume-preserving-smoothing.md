# Volume-Preserving Smoothing

## The Clinical Imperative: Preserving Prescription Volume

In radiation therapy, a bolus is a prescribed prescription thickness—commonly $5.0\text{ mm}$, $7.0\text{ mm}$, or $10.0\text{ mm}$ of tissue-equivalent material. This thickness is entered into Monte Carlo or Collapsed Cone Convolution dose calculation engines to produce an approved clinical treatment plan.

If the fabricated bolus deviates from this thickness:
- **Thinning ($< t_{\text{prescribed}}$)**: Underdosing of the superficial tumor bed, increasing the risk of local recurrence.
- **Thickening ($> t_{\text{prescribed}}$)**: Excessive radiation attenuation, pulling the target isodose lines shallower and underdosing deep margins while overdosing normal tissues.

Traditional mesh smoothing tools rely on **Laplacian vertex averaging**, which inexorably shrinks high-curvature anatomical regions (such as the tip of the nose, helix of the ear, or mental ridge of the chin). On a $5\text{ mm}$ ear bolus, naive smoothing frequently reduces volume by **15% to 25%**, rendering the physical bolus clinically unacceptable.

Fabolus addresses this through a specialized **volumetric morphological level-set pipeline** that eliminates CT slice stair-stepping while strictly preserving prescription volume.

---

## The Algorithmic Pipeline

Fabolus implements a continuous 4-stage morphological filter in `Fabolus.Core.Features.Smoothing`:

```
Raw Voxelized Mesh 
       │
       ▼
[Stage 1: Morphological Closing (OffsetDouble)]
       ├── Outward Offset (+d) bridges slice grooves
       └── Inward Offset (-d) restores true contour
       │
       ▼
[Stage 2: Over-Erosion Guard]
       └── Verifies mesh has not collapsed; aborts gracefully if over-eroded
       │
       ▼
[Stage 3: Fine Inflation (Offset)]
       └── Micro-expansion (+0.1 to +0.2 mm) restores corner energy
       │
       ▼
[Stage 4: Adaptive Resizing / Decimation]
       └── Downsamples to target triangle count without loss of geometric fidelity
       │
       ▼
Smoothed Clinical Solid (ΔV < 0.5%)
```

<!-- IMAGE_PLACEHOLDER: [Figure 4.1: The Smoothing Control Drawer. Screenshot showing parameter sliders (Intensity, Iterations, Inflation, Remesh Ratio, Resolution) alongside the Apply Smoothing action button and clinical volume delta indicator. Dimensions: 400x600px.] -->

### Step 1: Morphological Closing via `MR.doubleOffsetMesh`
Rather than shifting individual vertices in Euclidean space, the mesh is converted to a continuous implicit distance field:
$$\mathcal{M}_{\text{closed}} = (\mathcal{M} \oplus d) \ominus d$$
- **Dilation ($\oplus d$)**: Surfaces expand outward by distance $d$ (`Intensity`). This closes sharp, narrow grooves created by axial CT slicing.
- **Erosion ($\ominus d$)**: Surfaces contract inward by $-d$. Flat and broad anatomical features return precisely to their original coordinates, while staircase peaks are rounded down.

### Step 2: Over-Erosion Safety Guard
If an inexperienced user specifies an excessively high intensity on a delicate structure (e.g. Intensity = $5.0\text{ mm}$ on a $3\text{ mm}$ bolus), the inward erosion could collapse the geometry entirely. Fabolus monitors face counts:
```csharp
if (currentMesh.TriangleCount == 0) {
    return new Error("Smoothing.OverEroded", 
        "The mesh collapsed due to high intensity. Try reducing Iterations or Intensity.");
}
```
This protects the user from silent geometry destruction and application crashes.

### Step 3: Micro-Inflation
Rounding sharp $90^\circ$ voxel corners mathematically causes an infinitesimal volumetric deficit proportional to the voxel step volume. Fabolus allows a calibrated micro-inflation offset (`Inflation`, default `+0.1 mm` to `+0.2 mm`) that restores this lost volume perfectly.

### Step 4: Adaptive Decimation
The resulting dense level-set surface is retriangulated using edge-collapse decimation. The target triangle count is controlled by `RemeshRatio`:
$$N_{\text{target}} = N_{\text{initial}} \times \max(\text{RemeshRatio}, 1.0)$$
This ensures the output file is clean, compact, and fast to slice.

---

## Smoothing Controls & Recommended Clinical Settings

| Parameter | Default | Clinical Range | Purpose & Guidelines |
| :--- | :--- | :--- | :--- |
| **Intensity ($mm$)** | `1.5 mm` | `0.8 – 2.0 mm` | **Primary smoothing power**. Should be set approximately equal to or slightly higher than the original simulation CT slice thickness (e.g. $1.5\text{ mm}$ for a $1.25\text{ mm}$ CT). |
| **Iterations** | `1` | `1 – 2` | Number of morphological passes. 1 pass is ideal for 95% of clinical cases. 2 passes should be reserved for coarse $3\text{ mm}$ CT slices. |
| **Inflation ($mm$)** | `0.2 mm` | `0.0 – 0.3 mm` | Volumetric calibration. Increase slightly if post-smoothing volume shows a negative delta. |
| **Remesh Ratio** | `1.0` | `1.0 – 2.0` | Output mesh resolution. `1.0` maintains identical triangle density; `1.5` yields smoother visual contours on complex facial topography. |
| **Resolution ($mm$)**| `1.0 mm` | `0.5 – 1.5 mm` | Level-set voxel grid resolution. Smaller values increase geometric precision at the cost of computation time. |

---

## Clinical Verification & Quality Assurance Tools

Before committing any smoothed bolus to mould generation, the clinician must verify adherence to clinical tolerances. Fabolus provides three synchronized verification modes:

<!-- IMAGE_PLACEHOLDER: [Figure 4.2: Signed Distance Heatmap Visualization. 3D render of a nasal bolus with color-mapped surface deviation from -1.5 mm (red) to +1.5 mm (blue), highlighting preserved volume along the dorsal bridge. Dimensions: 1000x550px.] -->

### 1. Signed Distance Deviation Heatmap
The heatmap evaluates the shortest signed Euclidean distance from every point $\mathbf{p}$ on the smoothed mesh to the original raw CT surface $\mathcal{S}_{\text{original}}$:
$$d(\mathbf{p}, \mathcal{S}) = \operatorname{sgn}(\mathbf{n} \cdot (\mathbf{p} - \mathbf{q})) \min_{\mathbf{q} \in \mathcal{S}} \|\mathbf{p} - \mathbf{q}\|$$
- **Green**: Zero deviation ($|d| \le 0.1\text{ mm}$).
- **Blue**: Surface expansion ($d > +0.1\text{ mm}$), where voxel valleys were filled.
- **Red**: Surface contraction ($d < -0.1\text{ mm}$), where voxel crests were rounded.
- **Sensitivity Slider**: Adjusts the dynamic color gamut (e.g. $\pm 0.4\text{ mm}$ for tight QA).

<!-- IMAGE_PLACEHOLDER: [Figure 4.3: Ghost Overlay Comparison. Semi-transparent phantom overlay comparing original voxelized CT perimeter against smoothed solid, demonstrating boundary adherence along the inner skin contact face. Dimensions: 900x500px.] -->

### 2. Ghost Mode
Renders a translucent, high-contrast phantom silhouette of the pre-smoothed input mesh over the smoothed model. This provides instantaneous visual confirmation that critical anatomical edges (e.g. eye socket margins, tragus of the ear) have not drifted.

<!-- IMAGE_PLACEHOLDER: [Figure 4.4: 3D Cutting Plane and Cross-Sectional Contour View. Slicing view showing inner and outer bolus contours with interactive 3D manipulator and thickness dimension callouts. Dimensions: 1000x550px.] -->

### 3. Interactive Cross-Section Cutting Plane
- Click **Cutting Plane** to activate the 3D translation manipulator.
- Drag the handle vertically along the $Z$-axis to slice dynamically through the volume.
- The viewport displays high-visibility 2D cross-sectional contours:
  - **Inner Contour**: Skin-facing contact border.
  - **Outer Contour**: Superficial radiation entrance border.
- Directly inspect the distance between inner and outer contours to verify uniform prescription thickness throughout the treatment volume.
