# Sacrificial Mould Design

## What is a Sacrificial Mould?

A **sacrificial mould** is a negative enclosing matrix printed specifically to cast liquid silicone into the exact geometric form of the prescribed patient bolus. Once the liquid silicone polymerizes into an elastomer, the mould is sacrificed:
- **Water-Soluble Dissolution**: Printed in Polyvinyl Alcohol (PVA/PVOH), the mould dissolves entirely in warm water, releasing the silicone with zero mechanical force or risk of tearing.
- **Mechanical Break-Away / Peeling**: Printed in thin-wall PLA or PETG, the mould is notched and peeled away or disassembled along designed parting lines.

Manually engineering a negative casting mould in general-purpose CAD software (such as Fusion 360, SolidWorks, or Blender) requires complex offsetting, boolean cuts, alignment sprues, and manifold verification—often taking 2 to 4 hours per patient. **Fabolus automates this entire pipeline into a single-click parametric workflow.**

---

## Three Distinct Mould Geometries

Fabolus implements three specialized mould generation strategies to balance print speed, material consumption, and anatomical complexity:

<!-- IMAGE_PLACEHOLDER: [Figure 6.1: Mould Geometry Comparison. 3D renders comparing Convex Hull, Concave Shadow, and Contoured Shell moulds generated around the same auricular (ear) bolus, highlighting external volume and footprint differences. Dimensions: 1000x450px.] -->

### 1. Convex Hull Mould ([`ConvexMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L59))
- **Mathematical Principle**: Computes the 2D convex hull of all bolus vertices projected onto the $XY$ ground plane using Andrew's monotone chain algorithm:
  $$\mathcal{H}_{XY} = \operatorname{Conv}(\pi_{XY}(\mathcal{V}))$$
  The hull polygon is expanded outward by `OffsetXY` via Clipper2 polygon offsetting and extruded vertically:
  $$Z \in [Z_{\min} - \text{OffsetBottom}, \; Z_{\max} + \text{OffsetTop}]$$
- **Clinical Best Use**: Small to medium-sized boluses on relatively planar anatomy (chest wall, forehead, parotid, or chin).
- **Key Advantage**: Produces a solid, flat-bottomed rectangular block that stands upright with maximum stability on the 3D printer bed and laboratory casting bench.

### 2. Concave Shadow Mould ([`ConcaveMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L83))
- **Mathematical Principle**: Projects all triangular silhouette edges onto the $XY$ plane and merges them into a 2D non-convex union boundary ("shadow outline"). The contour is offset outward by `OffsetXY` and extruded vertically.
- **Clinical Best Use**: Asymmetric, curved, or L-shaped boluses (e.g. clavicle, shoulder-neck wraps, or unilateral cheek-lip boluses).
- **Key Advantage**: Eliminates unnecessary corner material, saving **20% to 45%** print time and filament compared to the convex hull.

### 3. Contoured Shell Mould ([`ContouredMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L107))
- **Mathematical Principle**: Generates an omnidirectional 3D volumetric shell offset directly in level-set space:
  $$\mathcal{M}_{\text{shell}} = \mathcal{M} \oplus \text{OffsetXY}$$
- **Clinical Best Use**: Very large, highly curved boluses (full scalp helmets, circumferential neck collars).
- **Key Advantage**: Uniform wall thickness across all 3 dimensions. Consumes minimal filament and dissolves rapidly in water baths.

---

## Clearance Offsets & Engineering Parameters

| Offset Parameter | Default | Recommended Range | Engineering Purpose & Tradeoffs |
| :--- | :--- | :--- | :--- |
| **Offset XY ($mm$)** | `2.0 mm` | `1.6 – 3.0 mm` | **Lateral wall thickness**. Must withstand the hydrostatic pressure of viscous liquid silicone during injection without outward bulging. Minimum recommended for FDM is 4 perimeters ($1.6\text{ mm}$ with a $0.4\text{ mm}$ nozzle). |
| **Offset Bottom ($mm$)** | `2.0 mm` | `2.0 – 4.0 mm` | **Base plate thickness**. Provides rigid bed adhesion and seals the bottom of the cavity. Must be thick enough to anchor the bottom injection sprue. |
| **Offset Top ($mm$)** | `2.0 mm` | `2.0 – 4.0 mm` | **Top cap thickness**. Encloses the ceiling of the cavity and anchors the exit chimneys of degassing vents. |

<!-- IMAGE_PLACEHOLDER: [Figure 6.2: Live Transparent Preview in Fabolus. Viewport screenshot showing semi-transparent cyan mould shell overlaid on the opaque bolus with clearance offset dimensions labeled. Dimensions: 900x550px.] -->

---

## Two-Stage Architecture: Live Preview vs. Committed Bake

To maintain high interactive framerates while adjusting settings, Fabolus splits mould computation into two stages:

### Stage 1: Lightweight Live Preview (`MouldDefinition.Generate`)
As you change mould types or adjust the offset sliders, Fabolus computes only the outer enclosing shell. It renders this shell as a translucent cyan glass volume in the DirectX viewport. You can inspect wall thickness and confirm that no anatomical feature pierces the boundary.

### Stage 2: Full Committed CSG Boolean (`MouldDefinition.Apply`)
When you click **Generate Mould**, Fabolus executes the full Constructive Solid Geometry (CSG) pipeline:

$$\mathcal{M}_{\text{final}} = \left( \mathcal{M}_{\text{shell}} \setminus \mathcal{M}_{\text{bolus}} \right) \setminus \bigcup_{i=1}^{k} \mathcal{C}_i$$

1. **Cavity Subtraction**: The smoothed bolus is subtracted from the solid mould shell, hollowing out the internal casting chamber.
2. **Channel Coring**: Every placed air channel and injection sprue is subtracted from the mould solid, coring out fluid tunnels that connect the cavity to the outside environment.
3. **Topology Validation**: The resulting boolean geometry is verified for watertightness and manifoldness.

<!-- IMAGE_PLACEHOLDER: [Figure 6.3: Baked Sacrificial Mould Interior. Cross-sectional cutaway render showing the hollow negative bolus cavity and intersecting air channel tunnels ready for injection. Dimensions: 900x500px.] -->

---

## The Non-Destructive "Clear Mould" Workflow

Because Fabolus uses a **Command-Replay architecture**, generating a mould does not permanently destroy your pre-mould mesh:
- If you notice an air vent is misaligned or wish to switch from a Convex Hull to a Concave Shadow mould, simply click **Clear Mould**.
- Fabolus instantly purges the `MouldDefinition` command from metadata and restores your pristine smoothed bolus.
- Make your adjustments and click **Generate Mould** again with zero geometric degradation.
