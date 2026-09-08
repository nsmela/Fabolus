# Sacrificial Mould Design

The **mould** tab builds a mould shell around the bolus and, on **Generate Mould**, subtracts the bolus cavity and the placed air channels from it. The result is a mould that is later printed and cast in silicone outside Fabolus.

---

## Mould shape

Choose one of three shapes (default `Concave`):

- **Convex** — footprint is the convex hull of the bolus (a box-like shell with straight outer walls).
- **Concave** — footprint follows the bolus's top-down silhouette, trimming empty corners.
- **Contoured** — an offset shell that follows the bolus contour.

<!-- IMAGE_PLACEHOLDER: [Figure 7.1: Convex, Concave, and Contoured moulds generated around the same bolus.] -->

---

## Mould settings

| Control | Default | Range | What it does |
| :--- | :--- | :--- | :--- |
| **Wall Thickness** | `2.5 mm` | `0.5`–`15 mm` | Thickness of the mould's outer walls. Should be at least as thick as your printer's shell thickness (typically shell lines × line thickness). |
| **Base Height** | `5.0 mm` | `2`–`20 mm` | Height of the solid base below the cavity. |
| **Trough Depth** | `0.0 mm` | `0`–`20 mm` | Depth of a reservoir recessed into the top face (`0` = none). |
| **Trough Offset** | `2.5 mm` | `0.5`–`15 mm` | Margin used when shaping the trough. |
| **Trough Shape** | `Footprint` | — | `Footprint` (basin across the top, inset from the wall) or `Channels` (basin only where the channels surface). |

Defaults and ranges come from `MouldPreferences`; the tab seeds these from your saved preferences.

> [!TIP]
> **Sizing Wall Thickness**: The wall thickness should be at least as thick as your 3D printer's shell thickness—usually calculated as the number of shell lines (perimeters) multiplied by the line thickness (extrusion width). A solid shell ensures watertight mould walls and avoids leakage during silicone casting.

As you change the shape or settings, a semi-transparent preview shows the mould around the bolus so you can confirm coverage before generating.

<!-- IMAGE_PLACEHOLDER: [Figure 7.2: Semi-transparent mould preview around a bolus in the viewport.] -->

---

## Generating and clearing the mould

- **Generate Mould** subtracts the bolus cavity and all placed air channels from the shell, producing the solid mould.
- **Clear Mould** removes the generated mould and returns to the bolus, so you can change settings or channels and generate again.

The mould is recorded as a command in the mesh's history (priority after the transform stage), so changing an earlier step such as rotation invalidates a previously generated mould.

<!-- IMAGE_PLACEHOLDER: [Figure 7.3: Cross-section of a generated mould showing the bolus cavity and channel openings.] -->
