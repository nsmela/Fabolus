# Print Orientation & Overhangs

## Why Print Orientation Matters

Whether 3D printing a sacrificial mould in water-soluble PVA or rigid PLA/PETG, surface quality and support generation depend heavily on the model's orientation relative to the build plate:

1. **Minimizing Support Scars**: Faces requiring support structures inevitably exhibit surface roughness and interface scarring upon support removal.
2. **Skin-Contact Surface Integrity**: In bolus casting, the mould's internal cavity reproduces the patient-facing skin surface. Any support scarring inside the cavity will translate directly to rough silicone, causing discomfort or air gaps against the patient's skin.
3. **Fluid Escape Direction**: In mould casting, orienting the highest anatomical point upward ensures that rising air bubbles naturally travel toward top-mounted vent channels during silicone injection.

---

## The Overhang Angle Heatmap

The **rotate** feature in Fabolus calculates the angle between each surface triangle's normal vector $\vec{n}$ and the downward vertical build direction $-\hat{z}$:

$$\theta = \arccos\left(\frac{\vec{n} \cdot (-\hat{z})}{\|\vec{n}\|}\right)$$

Triangles are dynamically colored in the 3D viewport based on user-defined thresholds:

- **Safe Zones (Green)**: $\theta < \theta_{\text{warning}}$ (Angles self-supporting on typical FDM/SLA printers, typically $0^\circ – 45^\circ$).
- **Warning Zones (Yellow)**: $\theta_{\text{warning}} \le \theta < \theta_{\text{critical}}$ (Angles requiring slow print speeds, cooling, or micro-supports, typically $45^\circ – 65^\circ$).
- **Critical Overhang Zones (Red)**: $\theta \ge \theta_{\text{critical}}$ (Steep overhangs that will drop, droop, or fail without dense support structures, typically $> 65^\circ$).

---

## Using the Orientation Tools

### 1. Interactive 3D Rotation Gizmo
- Click directly in the 3D viewport to engage the multi-axis rotation rings ($X, Y, Z$).
- Drag to rotate the bolus dynamically; the overhang color gradient recalculates in real time.

### 2. Precise Slider Adjustments
- Fine-tune rotation angles in degrees using the dedicated $X$, $Y$, and $Z$ numeric sliders or text boxes.
- Click **Reset** to return the mesh to its original coordinate alignment.

### 3. Golden Rule of Bolus Orientation
> [!TIP]
> **Orient the skin-contact surface facing UPWARDS** (or tilted slightly away from the bed).
> 
> By keeping the critical patient-facing cavity facing upward, all necessary 3D print supports will attach exclusively to the **outer, non-critical mould shell**, leaving the internal casting cavity flawless and mirror-smooth.
