# Air Channels & Degassing

## The Fluid Mechanics of Silicone Bolus Casting

Medical-grade addition-cure silicone elastomers (e.g., Smooth-On Dragon Skin 10 NV / 20 / 30, Ecoflex 00-30) possess high dynamic viscosities ($\mu \approx 15,000 – 25,000\text{ cP}$). When injected into an enclosed sacrificial mould cavity, fluid behavior is governed by the **Hagen-Poiseuille law for laminar viscous flow**:

$$\Delta P = \frac{8 \mu L Q}{\pi R^4}$$

*(where $\mu$ is dynamic viscosity, $L$ is channel length, $Q$ is volumetric flow rate, and $R$ is channel radius).*

<!-- IMAGE_PLACEHOLDER: [Figure 7.1: Fluid Mechanics of Silicone Mould Injection. Diagram illustrating laminar upward filling from bottom sprue, air displacement, and void formation at unvented anatomical peaks. Dimensions: 800x450px.] -->

### Critical Fluid Vulnerabilities:
1. **The $R^4$ Choke Effect**: Fluid resistance scales inversely with the **fourth power of the radius**. A $2\text{ mm}$ diameter channel requires **16 times more injection pressure** than a $4\text{ mm}$ channel. Undersized channels cause syringe stall, hydro-fracturing of thin mould walls, or incomplete filling ("short shots").
2. **Capillary Air Trapping**: As liquid silicone fills a mould, air cannot compress out through solid 3D printed plastic walls. Any anatomical local peak that lacks an open atmospheric vent forms an airtight cushion that stops silicone dead in its tracks.
3. **Internal Air Void Dosimetry**: A trapped air pocket as small as $2\text{ mm}$ inside a bolus creates a localized region of zero electron density, leading to severe radiation cold spots (up to 12% dose deficit) directly over malignant tissue.

---

## The Three Specialized Channel Geometries

Fabolus provides three distinct air channel primitives in `Fabolus.Core.Features.AirChannels`, designed to handle every anatomical topography:

<!-- IMAGE_PLACEHOLDER: [Figure 7.2: Three Channel Types in Fabolus. 3D render showing Straight, Angled, and Painted channels with geometric parameters (Tip Diameter, Cone Length, Arc Radius, Channel Diameter) annotated. Dimensions: 900x500px.] -->

### 1. Straight Air Channel ([`StraightAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L30))
- **Geometry**: A vertical cylinder running from the bolus surface straight up to the mould ceiling ($Z_{\max}$), with a tapered conical transition at the cavity interface.
- **Best For**: 
  - **Primary Injection Port**: Placed at the absolute lowest anatomical point ($Z_{\min}$) to establish a bottom-up flood.
  - **Apex Vents**: Placed at dominant anatomical peaks (e.g. tip of the nose, chin dome).
- **Key Parameters**:
  - **Tip Diameter**: Entrance orifice ($1.5 – 2.5\text{ mm}$). Kept small so the cured silicone sprue tears off cleanly with negligible surface scarring.
  - **Cone Length**: Height of the tapered transition ($3.0 – 5.0\text{ mm}$). Prevents turbulent shear stress during injection.
  - **Cylinder Diameter**: Main chimney tube ($4.0 – 6.0\text{ mm}$). Minimizes hydraulic flow resistance.
  - **Penetration Depth**: Default `1.0 mm`. Embeds the cone tip slightly below the target mesh boundary before subtraction. This ensures a clean, manifold boolean cut without paper-thin zero-thickness boundaries.

### 2. Angled Air Channel ([`AngledAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L74))
- **Geometry**: Starts precisely perpendicular (normal) to the local bolus surface ($\mathbf{n}_{\text{surface}}$), extends outward as a conical tip, and blends through a smooth, mathematically continuous 3D circular arc into a vertical $+Z$ exit tube.
- **Best For**: Steep lateral side walls (e.g. lateral alar flare of the nose, retroauricular groove behind the ear, or submental jawline).
- **The Problem It Solves**: If a user places a vertical straight channel on a 75° side wall, the channel glances off at an acute angle, producing a knife-edge boundary that breaks during printing. The Angled channel exits perpendicular to the surface, creating a robust, leak-proof port.

### 3. Painted (Path) Air Channel ([`PaintedAirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs#L145))
- **Geometry**: An extruded 3D curtain or contoured tube swept along an arbitrary freehand stroke painted directly onto the bolus mesh.
- **Best For**: Curved anatomical ridges (e.g. the outer helix rim of an ear, the dorsal ridge of a nose, or perimeter perimeter borders).
- **How It Works**: As the user drags the mouse cursor across the bolus in the viewport, Fabolus raycasts continuous 3D coordinates onto the underlying mesh triangles. The points are linked into a smooth 3D polyline and extruded vertically to the mould ceiling.

<!-- IMAGE_PLACEHOLDER: [Figure 7.3: Painting a Surface Path Channel on an Auricular Bolus. Viewport screenshot demonstrating the freehand paint tool snapping along the curved rim of an ear bolus, with the live extrusion preview displaying in real time. Dimensions: 900x550px.] -->

---

## Interactive Channel Placement Workflow

Fabolus streamlines channel authoring directly within the **mould** workspace:

1. **Activate Channel Mode**: In the left mould panel, switch to the **Channels** sub-tab.
2. **Select Type**: Choose **Straight**, **Angled**, or **Painted**.
3. **Live Hover Preview**: As you move the mouse across the bolus surface in the 3D viewport, Fabolus computes the local surface intersection and normal vector in real time. A translucent preview channel tracks your cursor, showing the exact size, orientation, and clearance.
4. **Click to Place**: Left-click to drop the channel. A visual anchor sphere marks its position.
5. **Inspect & Tweak**: Left-click any placed channel's anchor sphere to select it. The left parameter drawer immediately syncs to that channel's values, allowing you to edit diameters or delete misplaced vents with the `Delete` key.

---

## Clinical Placement Strategy (The "Bubbles Float Up" Protocol)

```
                            Atmospheric Vents (Top)
                                 │ │        │ │
                         ┌───────┴─┴────────┴─┴───────┐
                         │       ╭────────────╮       │
                         │       │   CAVITY   │       │
                         │       ╰──────┬─────╯       │
                         └──────────────┼─────────────┘
                                        │
                               Injection Sprue (Bottom)
```

1. **Rule 1: One Bottom Sprue to Rule Them All**
   Always place the primary injection port at the **lowest $Z$ point of the cavity**. Never pour liquid silicone from the top like a cake batter—this folds air into the cavity. Inject from the bottom so the rising fluid front sweeps all air upward ahead of it.
2. **Rule 2: Every Local Maximum Must Have a Vent**
   Rotate your model in 3D and locate every anatomical peak or crest. If liquid silicone rises past a ridge, any air trapped beneath that ridge will remain forever. Place an Angled or Straight vent at every high point.
3. **Rule 3: Use Painted Vents for Sharp Rims**
   For thin, delicate ridges like the outer ear helix or tragus, paint a continuous path channel along the crest. This ensures air evacuates smoothly along the entire edge without requiring a dozen individual sprue dots.
