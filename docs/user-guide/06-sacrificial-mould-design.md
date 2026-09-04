# Sacrificial Mould Design

The **mould** tab builds a mould shell around the bolus and, on **Generate Mould**, subtracts the bolus cavity and the placed air channels from it. The result is a mould that is later printed and cast in silicone outside Fabolus.

---

## Mould shape

Choose one of three shapes (default `Concave`):

- **Convex** — footprint is the convex hull of the bolus (a box-like shell with straight outer walls).
- **Concave** — footprint follows the bolus's top-down silhouette, trimming empty corners.
- **Contoured** — an offset shell that follows the bolus contour.

<!-- IMAGE_PLACEHOLDER: [Figure 6.1: Convex, Concave, and Contoured moulds generated around the same bolus.] -->

---

## Mould settings

| Control | Default | Range | What it does |
| :--- | :--- | :--- | :--- |
| **Wall Thickness** | `2.5` | `0.5`–`15` | Thickness of the mould's outer walls. |
| **Base Height** | `5.0` | `2`–`20` | Height of the solid base below the cavity. |
| **Trough Height** | `0.0` | `0`–`20` | Depth of a reservoir recessed into the top face (`0` = none). |
| **Trough Offset** | `2.5` | `0.5`–`15` | Margin used when shaping the trough. |
| **Trough Shape** | `Footprint` | — | `Footprint` (basin across the top, inset from the wall) or `Channels` (basin only where the channels surface). |

Defaults and ranges come from `MouldPreferences`; the tab seeds these from your saved preferences.

As you change the shape or settings, a semi-transparent preview shows the mould around the bolus so you can confirm coverage before generating.

<!-- IMAGE_PLACEHOLDER: [Figure 6.2: Semi-transparent mould preview around a bolus in the viewport.] -->

---

## Generating and clearing the mould

- **Generate Mould** subtracts the bolus cavity and all placed air channels from the shell, producing the solid mould.
- **Clear Mould** removes the generated mould and returns to the bolus, so you can change settings or channels and generate again.

The mould is recorded as a command in the mesh's history (priority after the transform stage), so changing an earlier step such as rotation invalidates a previously generated mould.

<!-- IMAGE_PLACEHOLDER: [Figure 6.3: Cross-section of a generated mould showing the bolus cavity and channel openings.] -->
