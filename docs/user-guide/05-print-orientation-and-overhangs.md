# Print Orientation & Overhangs

The **rotate** tab lets you orient the mesh and see how steep its surfaces are, which affects how it prints. Orientation matters here because supports that print inside a mould cavity are hard to remove; printing and support generation themselves happen later in a slicer.

---

## Overhang colouring

When the rotate tab is active, Fabolus colours the mesh by surface angle using a gradient between two thresholds:

- **Warning angle** — default `45°`.
- **Critical angle** — default `65°`.

Surfaces shallower than the warning angle, between the two, and steeper than the critical angle are shown across the gradient, so you can see at a glance which faces are steep. The two thresholds come from `RotationPreferences` and are adjustable (kept at least 5° apart, within `30°`–`90°`).

<!-- IMAGE_PLACEHOLDER: [Figure 5.1: The rotate tab with the overhang gradient applied to a bolus mesh.] -->

---

## Rotating the mesh

- Drag the X, Y, or Z rotation rings around the mesh to turn it, or enter angles into the X/Y/Z fields.
- The overhang colouring updates as you rotate.
- **Reset** returns the mesh to its original orientation.

A rotation is recorded as a transform command in the mesh's history, so it is preserved through later steps and can be replayed or replaced.
