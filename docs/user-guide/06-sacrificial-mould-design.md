# Sacrificial Mould Design

## What is a Sacrificial Mould?

A **sacrificial mould** is a negative enclosing shell printed specifically to cast liquid silicone into the exact shape of the prescribed bolus. After the silicone polymerizes and cures, the surrounding mould is sacrificed—either dissolved away in warm water (using water-soluble PVA/PVOH) or carefully peeled/broken away along designed release lines (using thin-wall PLA or PETG).

Fabolus automates the generation of this mould, eliminating hours of manual CAD boolean subtraction, offset modeling, and sprue drafting.

---

## Mould Types in Fabolus

Fabolus offers three distinct mould generation algorithms in the **mould** feature:

```
    Convex Hull Mould                Concave Shadow Mould              Contoured Shell Mould
    ┌────────────────┐                 ╭───────╮                     ╭───────────────╮
    │  ╭──────────╮  │                ╭╯╭─────╮╰╮                   ╭╯ ╭───────────╮ ╰╮
    │  │  Cavity  │  │                │ │Cav. │ │                   │  │  Cavity   │  │
    │  ╰──────────╯  │                ╰╮╰─────╯╭╯                   ╰╮ ╰───────────╯ ╭╯
    └────────────────┘                 ╰───────╯                     ╰───────────────╯
(Simple, robust block)            (Tight XY boundary)             (Uniform 3D shell offset)
```

### 1. Convex Hull Mould ([`ConvexMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L59))
- **How it works**: Computes the 2D convex hull of the mesh's projection on the $XY$ plane, expands it by `OffsetXY`, and extrudes it vertically from $(Z_{\min} - \text{OffsetBottom})$ to $(Z_{\max} + \text{OffsetTop})$.
- **Best for**: Small-to-medium boluses, flat chest-wall pads, or clinical sites with simple geometries.
- **Advantage**: Extremely sturdy, stands securely on the 3D printer bed and lab bench during casting.

### 2. Concave Shadow Mould ([`ConcaveMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L83))
- **How it works**: Computes the exact 2D outline (shadow silhouette) of the 3D bolus projected onto the $XY$ plane, applies a polygonal offset via Clipper2Lib, and extrudes vertically.
- **Best for**: L-shaped, curved, or asymmetric boluses (e.g. clavicle/shoulder boluses).
- **Advantage**: Saves 20–40% print time and filament compared to the convex hull by eliminating unnecessary exterior bulk.

### 3. Contoured Shell Mould ([`ContouredMouldDefinition`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/MouldDefinition.cs#L107))
- **How it works**: Creates an omnidirectional 3D volumetric offset shell around the entire bolus geometry.
- **Best for**: Very large boluses (full scalp, helmet, neck wraps).
- **Advantage**: Minimal material consumption, lightweight, easily dissolved in water baths.

---

## Offset Parameters & Recommendations

| Offset | Default | Recommended Range | Purpose |
| :--- | :--- | :--- | :--- |
| **Offset XY ($mm$)** | `2.0 mm` | `1.5 – 3.0 mm` | Lateral wall thickness. Must withstand hydrostatic pressure of injected silicone without bulging. |
| **Offset Bottom ($mm$)** | `2.0 mm` | `2.0 – 4.0 mm` | Base plate thickness. Provides adhesion to the print bed and seals the bottom of the cavity. |
| **Offset Top ($mm$)** | `2.0 mm` | `2.0 – 4.0 mm` | Top cap thickness. Secures the exit orifices of air vents and injection sprues. |

---

## The Generation Workflow

1. **Preview Shell**: When you select a mould type and adjust offsets, Fabolus renders a semi-transparent preview shell in the 3D viewport. This allows you to verify that walls are sufficiently thick.
2. **Channel Setup**: Position all required injection sprues and air vents (see [Air Channels & Degassing](07-air-channels-and-degassing.md)).
3. **Commit & Bake**: Click **Generate Mould**. Fabolus executes the CSG pipeline:
   $$\text{Mould}_{\text{final}} = \text{Mould}_{\text{shell}} \setminus \text{Bolus}_{\text{target}} \setminus \bigcup_{i} \text{Channel}_i$$
4. The result is a unified solid ready for slicing.
