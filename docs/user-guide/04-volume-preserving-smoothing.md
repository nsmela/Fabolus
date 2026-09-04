# Volume-Preserving Smoothing

## The Clinical Imperative: Preserving Prescription Volume

In radiotherapy, a prescribed bolus thickness (e.g., 5.0 mm or 10.0 mm) is selected during treatment planning to deliver a precise radiation dose to the skin while sparing deep critical structures. 

Traditional CAD smoothing tools (such as Laplacian smoothing or Taubin smoothing with poor parameter tuning) smooth sharp corners by pulling vertices toward their centroids. For thin boluses with high curvature (e.g. over the nose or ear), this causes **severe volumetric contraction**:
- The bolus becomes thinner than planned.
- Radiation attenuation decreases.
- The delivered skin dose deviates from the clinical target prescription.

Fabolus implements a specialized **morphological erosion-dilation smoothing pipeline** specifically designed to eliminate voxel stair-stepping while maintaining strict volume conservation.

---

## The Morphological Smoothing Pipeline

Fabolus operates in continuous level-set / distance-field voxel space through native MeshLib routines:

```
Raw Voxelized Mesh ──> [1. Dilate +d] ──> [2. Erode -d] ──> [3. Optional Inflation] ──> [4. Decimation / Remesh]
```

1. **Erosion-Dilation Cycle (`MR.doubleOffsetMesh`)**:
   - The surface is offset outward by distance $d$ (`Intensity`), closing concavities and bridging slice-stepping grooves.
   - The expanded surface is immediately offset inward by $-d$, restoring original boundary limits.
   - This morphological closing eliminates jagged step edges without causing the shrinking characteristic of vertex averaging.
2. **Fine Inflation**:
   - A microscopic positive offset (`Inflation`, default `0.1–0.2 mm`) compensates for any slight volume loss during sharp-feature rounding.
3. **Adaptive Decimation / Resize**:
   - The resulting voxel-generated surface is decimated back to a high-quality, uniform triangle mesh using the specified `Remesh Ratio`.

---

## Smoothing Controls & Parameters

| Parameter | Recommended Range | Description |
| :--- | :--- | :--- |
| **Intensity ($mm$)** | `0.8 – 2.0 mm` | The morphological offset distance. Set roughly equal to or slightly larger than the original CT slice thickness. |
| **Iterations** | `1 – 2` | Number of erosion-dilation passes. A single pass is usually sufficient. |
| **Inflation ($mm$)** | `0.0 – 0.3 mm` | Micro-dilation offset to counter volume drop on thin margins. |
| **Remesh Ratio** | `1.0 – 2.0` | Controls triangle density relative to the raw input mesh. Higher values capture smoother organic curves. |

---

## Verification & Clinical Quality Assurance (QA)

Fabolus provides three independent visualization modes to confirm that the smoothed bolus satisfies clinical tolerances before mould generation:

### 1. Signed Distance Deviation Heatmap
- Toggling **Heatmap** computes the exact point-to-surface Euclidean distance between the pre-smoothed input mesh and the smoothed mesh.
- **Color Scale**:
  - **Green**: Zero deviation ($\pm 0.1\text{ mm}$).
  - **Blue**: Local expansion (filling in a stair-step valley).
  - **Red**: Local reduction (rounding off a stair-step crest).
- Use the **Heatmap Sensitivity** slider to scale the color threshold (e.g., highlighting any deviations larger than $\pm 0.5\text{ mm}$).

### 2. Ghost Overlay
- Renders the pre-smoothed raw CT contour as a semi-transparent wireframe or phantom shell over the smoothed solid.
- Allows immediate visual verification of feature adherence along critical skin contact borders.

### 3. Interactive Cross-Section Cutting Plane
- Enables an interactive 3D translation manipulator aligned along the $Z$-axis.
- Slices through the bolus in real time, rendering high-contrast 2D contour lines.
- Clinicians can drag the cutting plane through the bolus to directly measure and verify internal wall thickness from inner skin-facing edge to outer surface.
