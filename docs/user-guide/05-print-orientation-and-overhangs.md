# Print Orientation & Overhangs

## The Physics of 3D Printing Overhangs

In extrusion-based additive manufacturing (Fused Deposition Modeling / FFF) and vat photopolymerization (SLA / DLP), each successive layer must be supported by the layer beneath it. When a surface slopes away from the vertical axis, the layer extrusion overhangs the previous perimeter by an offset:

$$\Delta x = h \cdot \cot(\theta)$$

*(where $h$ is layer height and $\theta$ is the angle relative to the downward vertical build vector $-\hat{z}$).*

<!-- IMAGE_PLACEHOLDER: [Figure 5.1: Overhang Physics in Additive Manufacturing. Diagram illustrating layer deposition angle θ relative to vertical -Z axis, showing stable bead overlap at 40° vs molten bead drooping and stepping at 70° without supports. Dimensions: 800x400px.] -->

### The Clinical Stakes in Sacrificial Moulding
In standard 3D printing, support structures are simply detached and sanded. In sacrificial bolus moulding, however, the **internal mould cavity directly determines the patient's skin contact surface**:
1. **The Internal Support Trap**: If a bolus is oriented such that its internal cavity ceiling has steep overhangs, the slicer will generate support columns **inside the mould cavity**. Removing supports from inside a narrow hollow cavity leaves rough plastic nubs and scarring.
2. **Skin Irritation**: When silicone is cast against rough support remnants, the cured bolus will transfer that sandpaper-like roughness onto sensitized, irradiated patient skin.
3. **Air Void Formation**: Micro-roughness on the cavity ceiling traps rising air bubbles, preventing complete silicone filling.

---

## The Dynamic Overhang Angle Heatmap

Fabolus eliminates guesswork by computing the exact angle between each triangular face normal $\mathbf{n}$ and the downward gravity build vector $-\hat{\mathbf{z}}$:

$$\theta = \arccos\left(\frac{\mathbf{n} \cdot (-\hat{\mathbf{z}})}{\|\mathbf{n}\|}\right)$$

In the **rotate** tab, Fabolus evaluates this angle across all faces in real time, projecting a smooth, dynamic vertex color gradient onto the 3D model:

```
0° (Directly upward) ────────────────────── Safe Zone (Green: Self-Supporting)
                                             │
45° (Warning Threshold) ─────────────────── Warning Zone (Yellow: Micro-sag)
                                             │
65° (Critical Threshold) ────────────────── Critical Zone (Red: Dense Supports Required)
                                             │
180° (Directly downward) ────────────────── Ceiling (Deep Red: Cannot print unsupported)
```

<!-- IMAGE_PLACEHOLDER: [Figure 5.2: The Rotate Tool Interface. Screenshot showing the 3-axis rotation gizmo encircling the bolus in the SharpDX viewport, with the degree sliders (X, Y, Z) and Warning/Critical angle adjustment controls highlighted in the left panel. Dimensions: 900x550px.] -->

### Threshold Controls
In the left tool sidebar, users can calibrate the gradient thresholds to match their clinic's 3D printer capabilities:
- **Warning Angle ($\theta_{\text{warning}}$)**: Default `45.0°`. The threshold where surface layer quality begins to degrade.
- **Critical Angle ($\theta_{\text{critical}}$)**: Default `65.0°`. The absolute physical limit beyond which molten filament drops into free air without support structures.

---

## Manipulation Tools: Gizmos & Slider Controls

Fabolus offers two complementary orientation workflows:

### 1. The Interactive 3D Rotation Gizmo
- Hovering over the bolus reveals three orthogonal color-coded rotation rings:
  - **Red Ring**: $X$-axis pitch rotation.
  - **Green Ring**: $Y$-axis roll rotation.
  - **Blue Ring**: $Z$-axis yaw/azimuth rotation.
- Click and drag any ring to rotate the model fluidly. The overhang gradient recalculates continuously at 60 FPS, giving instantaneous feedback on support zones.

### 2. Precise Numeric Degree Sliders
- Enter exact rotational values (e.g. `+15.0°`) or drag the fine-step sliders.
- **Reset**: Instantly returns the mesh to its original CT coordinate alignment.

---

## The Golden Rules of Clinical Bolus Orientation

<!-- IMAGE_PLACEHOLDER: [Figure 5.3: Orientation Comparison for an Ear Bolus. Side-by-side comparison: Suboptimal orientation requiring internal cavity supports vs. Optimized orientation where the skin-contact face points upward, placing all support attachments harmlessly on the exterior mould shell. Dimensions: 1000x500px.] -->

### Rule 1: Point the Skin-Contact Cavity UPWARD
Always orient the bolus so that the concave, patient-facing contact surface points **upward toward $+Z$**.
- **Result**: The patient-contact surface is printed as an upward-facing top shell (clean, continuous, mirror-smooth).
- **Benefit**: Any support structures required by the slicer will attach exclusively to the **exterior non-critical mould shell**, which is later discarded.

### Rule 2: Align the Longest Axis Along the Print Bed
For large boluses (e.g. scalp, chest wall, or extremity wraps), rotate the model so its longest planar dimension aligns with the printer's build plate diagonal. This minimizes $Z$-height, reducing total print time and lowering the center of gravity to prevent print detachment.

### Rule 3: Ensure Anatomical Peaks Point toward $+Z$
Ensure that the anatomical apex points vertically upward. As liquid silicone is injected into the bottom of the mould, air bubbles naturally rise along buoyant trajectories toward the highest point. An upward-pointing apex allows a single vertical vent channel to evacuate all trapped air completely.
