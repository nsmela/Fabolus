# Volume-Preserving Smoothing

A bolus mesh exported from a TPS is built from stacked CT slices, so its surface has stair-stepping ridges. Fabolus smooths those out while keeping the mesh close to its original volume, which matters because the bolus thickness is clinically prescribed.

---

## How it works

Rather than averaging vertex positions (which shrinks the mesh), Fabolus smooths with a two-step offset:

```
Step 1: offset outward (+d)          Step 2: offset inward (-d)
fills the gaps between CT steps       returns broad areas to their original position
        ┌──┐                                 ╭──╮
   ┌────┘  └────┐                      ╭─────╯  ╰─────╮
   │ Raw steps  │                      │ Smoothed     │
   └────────────┘                      ╰──────────────╯
```

Expanding then contracting by the same distance leaves flat and broad regions where they were, while the sharp slice corners are rounded off. This is a morphological offset (a double offset), computed by the geometry engine.

<!-- IMAGE_PLACEHOLDER: [Figure 4.1: The smoothing controls panel with the Apply Smoothing button.] -->

---

## Smoothing controls

The **smooth** tab exposes these parameters (defaults shown are the values Fabolus starts with):

| Control | Default | Range | What it does |
| :--- | :--- | :--- | :--- |
| **Intensity** | `2.0` | `0`–`20` | Offset distance used for the outward/inward passes. |
| **Iterations** | `1` | `0`–`10` | Number of smoothing passes. |
| **Inflation** | `0.1` | `0`–`1` | Small additional outward offset applied after smoothing. |
| **Remesh Ratio** | `2.0` | `1`–`10` | Target triangle count relative to the base mesh when remeshing. |
| **Resolution** | `1.0` | `0.5`–`4` | Voxel size used by the offset operation. |

Defaults and ranges come from `SmoothingPreferences`; the tab seeds these from your saved preferences each time it opens.

Click **Apply Smoothing** to run it. Smoothing is recorded as a command in the mesh's history, so re-applying it replaces the previous smoothing rather than stacking on top of it.

---

## Reviewing the result

The **smooth** tab provides display modes and overlays to check the smoothed mesh:

- **Heat Map** — colours the surface by how far each point moved relative to the pre-smoothing mesh.
- **Cross Section** — slices through the mesh so you can inspect wall thickness.
- **Ghost / comparison** — overlays the pre-smoothing shape (with an adjustable comparison factor) so you can see where the surface changed.

<!-- IMAGE_PLACEHOLDER: [Figure 4.2: Heat-map display of a smoothed bolus.] -->

<!-- IMAGE_PLACEHOLDER: [Figure 4.3: Cross-section display showing the inner and outer contours of the smoothed mesh.] -->
