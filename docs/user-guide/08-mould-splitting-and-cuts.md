# Mould Splitting & Demoulding

## The Physics of Undercuts & Demoulding

When casting with water-soluble Polyvinyl Alcohol (PVA), demoulding is trivial: the printed mould simply dissolves in a warm water bath, releasing the silicone bolus regardless of geometric complexity.

However, when printing sacrificial moulds in standard **rigid materials** (such as PLA or PETG), the physics of **draft angles and anatomical undercuts** become paramount:

$$\text{Draft Angle } \alpha = 90^\circ - \arccos\left(\frac{\mathbf{n} \cdot \mathbf{d}_{\text{pull}}}{\|\mathbf{n}\|}\right)$$

*(where $\mathbf{n}$ is the cavity surface normal and $\mathbf{d}_{\text{pull}}$ is the extraction direction).*

<!-- IMAGE_PLACEHOLDER: [Figure 8.1: Demoulding Undercut Problem. Diagram illustrating negative draft angles (undercuts) mechanically locking cured silicone inside a monolithic rigid mould vs. clean, non-destructive separation achieved with a 2-part split mould. Dimensions: 800x400px.] -->

### The Undercut Trap
If any region of the cavity has a negative draft angle ($\alpha < 0^\circ$) relative to the opening—common in the retroauricular crease behind the ear, nasal vestibules, or submental jawline folds—the cured silicone bolus becomes mechanically trapped inside the rigid plastic. Attempting to pull the silicone out forcefully will tear delicate elastomeric features or fracture the plastic, scratching the patient-contact face.

To cast these complex anatomical sites without expensive water-soluble filaments, the mould must be divided into a **two-part (or multi-part) split mould** along a designated **parting plane**.

---

## Planar Cutting in Fabolus ([`CutMeshFeature`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/CutSplit/CutMeshFeature.cs))

Fabolus provides an automated geometric cutting engine that divides any manifold mesh into matching top and bottom halves along an arbitrary 3D plane:

```
                  Top Mould Half
               ┌──────────────────┐
               │   ╭──────────╮   │
═══════════════╪═══╪══════════╪═══╪═══════════════  Parting Plane (Origin p0, Normal n)
               │   ╰──────────╯   │
               └──────────────────┘
                 Bottom Mould Half
```

<!-- IMAGE_PLACEHOLDER: [Figure 8.2: Interactive Planar Cutting in Fabolus. Viewport screenshot showing the adjustable cutting plane slicing through a mould with normal vector controls and real-time top/bottom preview. Dimensions: 900x550px.] -->

### The Algorithmic Pipeline
1. **Half-Space Construction**:
   Given a plane origin $\mathbf{p}_0$ and normal unit vector $\hat{\mathbf{n}}$, Fabolus constructs an oriented half-space bounding volume $\mathcal{B}_{\text{half}}$ sized to twice the maximum bounding dimension of the target mesh ($D = 2 \cdot \max(\Delta x, \Delta y, \Delta z)$).
2. **Quaternion Normal Alignment**:
   The bounding volume is rotated from standard $+Z$ to align with plane normal $\hat{\mathbf{n}}$ via the minimum-rotation quaternion:
   $$\mathbf{q} = \frac{\left( \hat{\mathbf{z}} \times \hat{\mathbf{n}}, \; 1 + \hat{\mathbf{z}} \cdot \hat{\mathbf{n}} \right)}{\left\| \left( \hat{\mathbf{z}} \times \hat{\mathbf{n}}, \; 1 + \hat{\mathbf{z}} \cdot \hat{\mathbf{n}} \right) \right\|}$$
   and translated to origin $\mathbf{p}_0$.
3. **Simultaneous Dual Boolean Execution**:
   The engine computes both mating solids simultaneously:
   $$\mathcal{M}_{\text{top}} = \mathcal{M} \cap \mathcal{B}_{\text{half}}$$
   $$\mathcal{M}_{\text{bottom}} = \mathcal{M} \setminus \mathcal{B}_{\text{half}}$$
4. **Metadata & Ingestion**:
   Both halves receive independent identifiers and are ingested into the workspace as `Mould (Top)` and `Mould (Bottom)`.

---

## Selecting the Optimal Parting Line

<!-- IMAGE_PLACEHOLDER: [Figure 8.3: Exploded View of a Two-Part Mould Assembly. 3D exploded render showing Top and Bottom halves, alignment seams, clamping perimeters, and internal silicone bolus core. Dimensions: 900x500px.] -->

When positioning the cutting plane, follow these clinical guidelines:

1. **Follow the Silhouette Line (Line of Draw)**:
   Position the parting plane through the widest perimeter of the anatomy so that both halves have only positive draft angles relative to their respective separation directions.
2. **Avoid Splitting Across Critical Treatment PTVs**:
   Liquid silicone can seep into microscopic parting gaps between clamped mould halves, forming a paper-thin "flash" ridge. If possible, angle the parting plane so that the seam lies on the **outer surface** or along peripheral margins rather than running directly across the central high-dose tumor bed.
3. **Align with the Flat Print Bed**:
   Splitting the mould with a horizontal plane ($XY$) allows both halves to sit flat on the 3D printer build plate, requiring zero external supports during printing.

---

## Laboratory Assembly, Sealing & Demoulding Protocol

### 1. Sealing the Parting Seam
- Inspect the mating faces of the printed mould halves. Lightly rub the flat parting surfaces on 400-grit sandpaper over a flat plate to eliminate any layer-stepping burrs.
- Apply a microscopic film of silicone-free petroleum jelly (Vaseline) or spray mold release along the parting contact lips to prevent flash leakage.
- **Do not allow petroleum jelly to enter the casting cavity itself**, as it will inhibit platinum silicone curing.

### 2. Clamping
- Secure the halves together using heavy-duty steel binder clips, screw clamps, or heavy rubber bands positioned at 20 mm intervals around the peripheral flanges.
- Ensure the clamping pressure is uniform to prevent parting line gaps during injection.

### 3. Demoulding
- Once the silicone has fully polymerized (confirming that excess silicone in the sprue is non-tacky and resilient):
  1. Remove all binder clips.
  2. Insert a flat, dull plastic pry tool (never a sharp razor that could gouge the silicone) into the parting seam and twist gently.
  3. The halves will pop open cleanly along the seam.
  4. Peel the silicone bolus out of the cavity.
  5. Trim any microscopic flash seams flush using fine iris scissors.
