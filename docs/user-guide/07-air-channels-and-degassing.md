# Air Channels & Degassing

## The Fluid Dynamics of Silicone Bolus Casting

Medical-grade addition-cure silicone (such as Smooth-On Dragon Skin 10/20/30 or Ecoflex 00-30) is poured or injected as a viscous liquid. During curing, two major defects can ruin a clinical bolus:
1. **Air Voids (Bubbles)**: Micro-bubbles trapped in the silicone alter radiological electron density, causing unexpected dosimetry shifts during radiation beam delivery.
2. **Incomplete Fill (Short Shot)**: Pockets of air trapped at the highest anatomical points prevent silicone from reaching the mould extremities, leaving missing chunks of bolus.

To achieve a 100% solid, bubble-free bolus, the mould requires strategically placed **injection sprues** (where silicone enters) and **degassing vents / risers** (where displaced air escapes).

---

## Channel Types in Fabolus

Fabolus provides three specialized channel types in the **mould** feature:

```
    Straight Channel                 Angled Channel                    Painted (Path) Channel
          │ │                              │ │                                  │ │
          │ │                              │ ╰╮                              ╭──╯ ╰──╮
         ╱   ╲                            ╱   ╰╮                            ╱         ╲
        ───────                          ───────                           ─────────────
 (Vertical Sprues & Vents)          (Normal-to-Vertical Arc)           (Contoured Ridge Degassing)
```

### 1. Straight Air Channel ([`StraightAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L30))
- **Geometry**: A vertical cylinder running from the bolus surface straight up to the top of the mould, with a tapered conical transition at the cavity interface.
- **Best for**: Main injection ports at the lowest anatomical points, or primary exit vents at the highest anatomical apex.
- **Parameters**:
  - `Tip Diameter`: Small orifice entering the bolus cavity (e.g. `2.0 mm`).
  - `Cone Length`: Height of the tapered transition (e.g. `4.0 mm`).
  - `Cylinder Diameter`: Main vertical channel diameter (e.g. `4.0 – 6.0 mm`).
  - `Penetration Depth`: How deeply the channel penetrates into the cavity before subtraction (ensures a clean manifold boolean cut without paper-thin zero-thickness walls).

### 2. Angled Air Channel ([`AngledAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L74))
- **Geometry**: Starts perpendicular (normal) to the local bolus surface, extends outward, and transitions through a smooth 3D arc into a vertical $Z$-up channel exiting the top of the mould.
- **Best for**: Steep side walls (e.g. lateral sides of a nose, ear concha, or chin) where a straight vertical channel would glance off or create fragile knife-edge overhangs.
- **Parameters**:
  - `Normal`: Auto-detected local surface normal vector.
  - `Tip Length`: Length of normal-oriented cone before curve initiation.
  - `Radius`: Curvature radius of the 3D transition arc.

### 3. Painted (Path) Air Channel ([`PaintedAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L145))
- **Geometry**: A freehand 3D curve painted directly onto the bolus surface, extruded upward to the mould ceiling.
- **Best for**: Long curved anatomical features (e.g., the helix of an ear, ridge of the nasal bridge, or peripheral perimeter margins) where air traps along an entire seam.
- **How to Use**: Click and drag the mouse across the bolus surface; Fabolus continuously raycasts points onto the mesh topology and sweeps a contoured channel along the stroke.

---

## Best Practices for Channel Placement

1. **The "Bottom-Up" Injection Rule**:
   - Always place the **main injection sprue at the lowest point ($Z_{\min}$)** of the cavity.
   - Injecting silicone from the bottom causes the liquid level to rise uniformly like a tide, pushing air upward and out.
2. **Every Peak Needs a Vent**:
   - Inspect the model for local maxima (anatomical peaks). Any peak without a vent will trap an air bubble.
   - Place a Straight or Angled vent at every local peak.
3. **Small Tips, Wide Chimneys**:
   - Keep the `Tip Diameter` small (`1.5 – 2.5 mm`) so the sprue breaks or trims off cleanly after curing with minimal scar tissue on the silicone.
   - Keep the main `Channel Diameter` wider (`4.0 – 6.0 mm`) to minimize fluid resistance and prevent surface tension from choking the venting air.
