# Cut & Split (Experimental)

> [!NOTE]
> **Experimental Feature**: Cut and Split are experimental. Each is its own tab, gated behind a preference that is off by default: **Cut view** (`cut_view_enabled`) and **Split view (for moulds)** (`split_view_enabled`). Turn them on in Preferences to add them to the tool navigation. See [Configuration & Preferences](../reference/configuration-and-preferences.md).

Two tools divide geometry into pieces:

- **Cut** divides a mesh along a flat plane into two halves. It works on meshes that are not moulds.
- **Parting Split** divides a generated **mould** into pieces along a parting line traced on the body inside it. It is useful when a mould will be printed in a rigid plastic and needs to come apart to release the cast part; with a dissolvable mould it is not needed.

---

## Cutting a mesh with a plane

1. With a mesh active, position the cutting plane using its X/Y/Z offset and pitch/yaw controls. **Reset Plane** places the plane at the mesh centre.
2. Click **Apply Cut**. Fabolus splits the mesh into two halves along the plane — the half on the side the plane normal points to is the "top".
3. Both halves are added to the workspace, named `{Original Name} (Top)` and `{Original Name} (Bottom)`, and the view returns to the mesh list.

<!-- IMAGE_PLACEHOLDER: [Figure 8.1: The cutting plane positioned through a mesh, with the resulting top and bottom halves.] -->

Each half is re-evaluated for topology and statistics, so you can check them in the Info Panel.

The **Show on** preference (`cut_view_scope`) decides which meshes offer the Cut tab. A mesh counts as a mould when it carries a generated mould, whatever it is named. The plane cut is not applied to moulds: on a mould, the tab points you to Parting Split instead.

---

## Splitting a mould along a parting line

Parting Split works on the mould shell, so generate a mould first (see [Sacrificial Mould Design](07-sacrificial-mould-design.md)). On a mesh without a mould, the tab asks you to generate one. The split runs in three steps, moved between with **Back** and **Next**.

### Step 1 of 3: Pull Direction

The body inside the mould is shaded by which half each face pulls away from, and the parting line is drawn over it.

- **Body assessment** and **Approach** summarise the body's shape, its contours and rims, and how the line is being traced. The approach is highlighted when the body would be better served by a different one.
- **Adjust the line**: drag a handle to move the line along the rim wall (it cannot leave the wall). Point at a section and a green marker shows where a click would divide it in two, adding a handle.
- **Free geodesic** lets sections take the shortest path across the whole body instead of across the rim wall. It is smoother, but on a tightly curved rim it can cut the corner onto the shell.
- **Remove selected handle**, **Smooth line** (press again for more) and **Reset line edits** tidy the line. **Reset Direction** starts the step again.

### Step 2 of 3: Parting Mesh

Fabolus builds the parting mesh — the thin surface that will cut the mould — and shows it inside a see-through mould. It should pass cleanly through the mould on every side. Green is its outer face and red the inner; red where you expect green means a flipped or self-intersecting surface. If the body has internal holes, the panel says how many, and an additional parting mesh is generated for each.

- **Cutter Thickness** (0.1–5.0 mm) sets how thick the cut is.
- **Loft to the mould** builds the parting mesh by lofting out to a ring on the mould, which gives a much flatter mating face than marching along the body's normals.

### Step 3 of 3: Split Result

Review the positive (blue) and negative (orange) halves, and choose how they are exported:

- **Separate files (one per half)**: an STL (or other mesh-only) export writes each piece to its own file, lettered beside the name you choose: `name_A.stl`, `name_B.stl`, and so on.
- **Single file (halves joined)**: the pieces are written together in one file.

Click **Apply Split** to add the result to the workspace as `{Mould Name} (Cut)` and open the mesh list. The split is kept in the mesh's history, so a project saved as 3MF reopens with the split rebuilt.

<!-- IMAGE_PLACEHOLDER: [Figure 8.2: The parting line on the body, the parting mesh inside the mould, and the two mould halves.] -->

> [!WARNING]
> Check each piece before printing: open it in the mesh list and confirm in the Info Panel that it is watertight. On some bodies the split cannot separate the mould, and Fabolus reports that rather than producing pieces.
