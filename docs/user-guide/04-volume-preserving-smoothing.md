# Volume-Preserving Smoothing

When 2D image contours from a CT scan are converted into a 3D mesh, the surface ends up with sharp features and stair-stepping ridges.

Standard 3D smoothing (such as vertex averaging) rounds off those bumps, but it can shrink the model—making walls thinner than planned. In radiation therapy, this is critical because the bolus thickness is specifically prescribed. Fabolus smooths out the sharp features while strictly preserving the original thickness and volume.

---

## How it works

Rather than averaging vertex positions (which can shrink the mesh), Fabolus smooths with a two-step offset:

```
Step 1: offset outward (+d)          Step 2: offset inward (-d)
fills the gaps between CT steps       returns broad areas to their original position
        ┌──┐                                 ╭──╮
   ┌────┘  └────┐                      ╭─────╯  ╰─────╮
   │ Raw steps  │                      │ Smoothed     │
   └────────────┘                      ╰──────────────╯
```

Expanding then contracting by the same distance leaves flat and broad regions where they were, while the sharp slice corners are rounded off. This is a morphological offset (a double offset: inflate then deflate), computed by the geometry engine.

> [!TIP]
> For additional background on morphological operations in 3D geometry, see [Mathematical Morphology (Wikipedia)](https://en.wikipedia.org/wiki/Mathematical_morphology) and [Geometry Engine](../architecture/03-geometry-engine.md).

<!-- IMAGE_PLACEHOLDER: [Figure 4.1: The smoothing controls panel with the Apply Smoothing button.] -->

---

## Smoothing controls

The **smooth** tab exposes these parameters (defaults shown are the values Fabolus starts with):

| Control | Default | Range | What it does |
| :--- | :--- | :--- | :--- |
| **Intensity** | `2.0 mm` | `0`–`20 mm` | Offset distance used to inflate then deflate the mesh. |
| **Inflation** | `0.1 mm` | `0`–`1 mm` | Small additional outward offset applied after smoothing to guarantee minimum thickness. |
| **Iterations** | `1` | `0`–`10` | Number of inflate/deflate passes. |
| **Triangle Ratio** | `2.0x` | `0.5`–`5x` | Target triangle count relative to the base mesh (`Remesh Ratio`). A larger amount allows a smoother mesh, but increases the file size and time to process and modify the mesh. |
| **Smoothness** | `1.0 mm` | `0.5`–`4 mm` | Grid sampling size in millimetres (`Resolution`). Smaller values produce finer detail and smoother curvature but take longer to compute. |

Defaults and ranges come from `SmoothingPreferences`. The tab seeds these from your saved preferences each time it opens, unless smoothing was already applied to the active mesh—in which case the controls show the settings that were actually applied.

Click **Apply Smoothing** to run it. Smoothing is recorded as a command in the mesh's history, so re-applying it replaces the previous smoothing rather than stacking on top of it.

> [!WARNING]
> **Downstream Invalidation**: Applying or re-applying smoothing can clear applied changes in the **mould**, **decals**, and **cut / split** views. Finalize smoothing before configuring moulds, air channels, or decals.

---

## Reviewing the result

The **smooth** tab provides display modes to inspect the smoothed mesh:

- **None** — normal 3D shaded mesh rendering.
- **Cross Section** — slices through the mesh with an adjustable plane to inspect wall thickness. The smoothed mesh is outlined in green, while the original unsmoothed mesh is outlined in red, showing the gap between the old mesh and the new one.
- **Heat Map** — colours the surface based on how far each point moved relative to the pre-smoothing mesh.

### Heat Map colour scale

The heat map displays surface deviation using a colour gradient:

```
  +2.0 mm (Blue)    ──  Outward expansion (added thickness)
  +1.0 mm (Cyan)    ──  Mild outward shift
   0.0 mm (Green)   ──  Zero deviation (original contour preserved)
  -1.0 mm (Yellow)  ──  Mild inward shift
  -2.0 mm (Red)     ──  Inward gap (shows where raw CT ridges were removed)
```

<!-- IMAGE_PLACEHOLDER: [Figure 4.2: Heat-map display of a smoothed bolus with the Blue (+2.0 mm) to Green (0.0 mm) and Red (-2.0 mm) deviation gradient.] -->

<!-- IMAGE_PLACEHOLDER: [Figure 4.3: Cross-section display showing the green smoothed contour against the red original contour to inspect the gap.] -->
