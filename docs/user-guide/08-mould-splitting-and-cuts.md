# Cut & Split

The **cut / split** view divides a mesh along a plane into two halves. It is useful when a mould will be printed in a rigid plastic and needs to come apart to release the cast part; with a dissolvable mould it is not needed.

> The cut and split views are gated by preferences (`cut_view_enabled` and `split_view_enabled`, both off by default). Enable them in Preferences to show the view. See [Configuration & Preferences](../reference/configuration-and-preferences.md).

---

## Cutting a mesh with a plane

1. With a mesh active, position the cutting plane using its X/Y/Z location and pitch/yaw orientation controls. **Reset** places the plane at the mesh centre.
2. Apply the cut. Fabolus splits the mesh into two halves along the plane — the half on the side the plane normal points to is the "top".
3. Both halves are added to the workspace, named `{Original Name} (Top)` and `{Original Name} (Bottom)`, and the view returns to the mesh list.

<!-- IMAGE_PLACEHOLDER: [Figure 8.1: The cutting plane positioned through a mesh, with the resulting top and bottom halves.] -->

Each half is re-evaluated for topology and statistics, so you can check them in the Info Panel.

---

## Current limitation

Splitting a **mould** is not yet implemented — applying the operation to a mesh whose name contains "Mould" reports that it is not available. The cut operation described above applies to non-mould meshes.
