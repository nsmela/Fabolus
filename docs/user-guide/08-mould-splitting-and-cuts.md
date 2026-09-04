# Mould Splitting & Demoulding

## When to Split a Mould

While water-soluble sacrificial moulds (printed in PVA) dissolve completely in warm water and can be cast as a single monolithic block, rigid moulds printed in PLA or PETG present a mechanical challenge: **anatomical undercuts**.

If an anatomical geometry (such as an ear canal, nasal flare, or chin depression) is enclosed inside a monolithic rigid mould, the cured silicone bolus cannot be extracted without tearing the silicone or fracturing the plastic against the patient-facing surface.

In these scenarios, the mould must be split into two or more mating halves (a two-part mould) along an optimal **parting plane**.

---

## Planar Cutting in Fabolus ([`CutMeshFeature`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/CutSplit/CutMeshFeature.cs))

The **Cut / Split** tool allows clinicians to split a mould with a user-positioned cutting plane:

```
          Top Mould Half
       ┌──────────────────┐
       │   ╭──────────╮   │
═══════╪═══╪══════════╪═══╪═══════  Parting Plane
       │   ╰──────────╯   │
       └──────────────────┘
         Bottom Mould Half
```

1. **Defining the Plane**:
   - The user selects a point on the mould and adjusts the cutting plane normal vector ($\vec{n}$) and vertical offset.
2. **Boolean Planar Division**:
   - Fabolus constructs oriented half-space bounding volumes.
   - The geometry engine computes both halves simultaneously:
     $$\text{Top} = \text{Mould} \cap \text{HalfSpace}^+$$
     $$\text{Bottom} = \text{Mould} \setminus \text{HalfSpace}^+$$
3. **Workspace Ingestion**:
   - Both halves are tagged with clean metadata (`Mould (Top)`, `Mould (Bottom)`) and added to the workspace as individual printable entities.

---

## Clamping, Sealing & Demoulding

### 1. Clamping the Halves
- The flat parting plane faces can be clamped using standard binder clips, rubber bands, or printed clamping jigs.
- A thin bead of petroleum jelly (Vaseline) or silicone-free mould release along the parting seam prevents liquid silicone flashing from leaking out between the halves.

### 2. Demoulding Protocol
- Once the silicone has fully polymerized (typically 4–16 hours depending on silicone chemistry and room temperature):
  1. Remove all clamps.
  2. Insert a plastic wedge or spatula into the parting seam and gently pry the mould halves apart.
  3. Lift the cured silicone bolus cleanly from the lower cavity.
  4. Use fine surgical scissors or a scalpel to flush-trim the small sprue and vent nubs.
