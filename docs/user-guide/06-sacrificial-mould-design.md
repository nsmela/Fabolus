# Sacrificial Mould Design

## What is a Sacrificial Mould?

A **sacrificial mould** is a hollow 3D-printed shell used to cast liquid silicone into the exact shape of your patient's bolus. Once the silicone cures into a soft rubber, the mould is "sacrificed" and removed:
- **Water-Soluble (PVA)**: Drop the mould into warm water, and the plastic completely dissolves away, leaving a pristine silicone bolus with zero pulling or risk of tearing.
- **Peel-Away (Thin PLA)**: The thin plastic shell is gently crushed or peeled away.

In traditional CAD software, designing a mould can take 2 to 4 hours of tedious work. **Fabolus automates this into a single click.**

---

## Choosing the Right Mould Shape

Fabolus offers three mould shapes. Choose the one that best matches your bolus:

<!-- IMAGE_PLACEHOLDER: [Figure 6.1: Mould Geometry Comparison. 3D renders comparing Convex Hull, Concave Shadow, and Contoured Shell moulds generated around the same auricular (ear) bolus, highlighting external volume and footprint differences. Dimensions: 1000x450px.] -->

### 1. Convex Hull (Box Mould)
- **What it looks like**: A clean, sturdy block with straight outer walls.
- **Best for**: Small to medium-sized boluses on flatter areas (chest wall, forehead, or chin).
- **Why use it**: It has a broad, flat bottom that sticks firmly to the 3D printer bed and won't tip over on your lab bench while you inject silicone.

### 2. Concave Shadow (Silhouette Mould)
- **What it looks like**: Hugs the top-down outline of the bolus, cutting away empty corners.
- **Best for**: Curved, L-shaped, or asymmetric boluses (shoulder wraps, collarbone, or cheek).
- **Why use it**: Saves **20% to 45% of printing time and filament** by not printing empty plastic in the corners.

### 3. Contoured Shell (Skin-Tight Mould)
- **What it looks like**: Follows every 3D contour of the bolus like a uniform jacket.
- **Best for**: Very large, wraparound shapes (full scalp helmets or neck collars).
- **Why use it**: Uses the least amount of filament and dissolves the fastest in water baths because the walls are uniformly thin everywhere.

---

## Mould Thickness Settings

Set how thick you want the mould walls in the left settings drawer:

| Setting | Default | Recommended | Why It Matters |
| :--- | :--- | :--- | :--- |
| **Offset XY** | `2.0 mm` | `1.6 – 2.4 mm` | **Side wall thickness**. Must be thick enough so the mould doesn't bulge when injecting thick silicone. (4 printer perimeters is ideal). |
| **Offset Bottom** | `2.0 mm` | `2.0 – 3.0 mm` | **Floor thickness**. Helps the mould stick securely to the 3D print bed and seals the bottom against leaks. |
| **Offset Top** | `2.0 mm` | `2.0 – 3.0 mm` | **Ceiling thickness**. Encloses the top of the cavity and anchors your air vent exits. |

<!-- IMAGE_PLACEHOLDER: [Figure 6.2: Live Transparent Preview in Fabolus. Viewport screenshot showing semi-transparent cyan mould shell overlaid on the opaque bolus with clearance offset dimensions labeled. Dimensions: 900x550px.] -->

---

## Live Preview & Generating the Mould

### 1. Transparent Cyan Live Preview
As you adjust wall thickness or switch mould styles, Fabolus shows you a **semi-transparent cyan preview**. You can rotate around the model to verify that the bolus is completely covered and has enough wall thickness on all sides.

### 2. Generate Mould (Carving the Cavity)
When you click **Generate Mould**, Fabolus:
1. Carves out the hollow cavity matching your smoothed bolus.
2. Drills out all your placed air vents and injection sprues.
3. Turns the preview into a solid, hollow printable mould ready for export.

<!-- IMAGE_PLACEHOLDER: [Figure 6.3: Baked Sacrificial Mould Interior. Cross-sectional cutaway render showing the hollow negative bolus cavity and intersecting air channel tunnels ready for injection. Dimensions: 900x500px.] -->

---

## The "Clear Mould" Safety Button

Made a mistake or want to adjust an air vent? 
- Click the **Clear Mould** button at any time.
- Fabolus instantly removes the mould shell and returns you to your clean, smoothed bolus.
- You can tweak your vents or change your settings and click **Generate Mould** again—nothing is permanently lost or damaged!

