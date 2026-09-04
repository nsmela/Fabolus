# Air Channels & Degassing

## Why Do We Need Channels and Vents?

Liquid medical silicone is thick—similar to warm honey. When you inject it into a closed 3D-printed plastic mould, two things happen:
1. **Air Needs an Escape Route**: 3D printed plastic is solid and airtight. As liquid silicone fills the cavity from below, all the air inside must have a way out. If an air bubble gets trapped at a high point, it leaves a hollow hole in your bolus, creating a radiation "cold spot."
2. **Tube Size Matters**: Trying to push thick silicone through a narrow tube ($1\text{ or }2\text{ mm}$) feels like trying to squeeze cold peanut butter through a coffee straw! You need adequately sized channels ($4\text{ to }5\text{ mm}$) so the silicone flows smoothly without blowing out the mould walls.

<!-- IMAGE_PLACEHOLDER: [Figure 7.1: Fluid Mechanics of Silicone Mould Injection. Diagram illustrating laminar upward filling from bottom sprue, air displacement, and void formation at unvented anatomical peaks. Dimensions: 800x450px.] -->

---

## The Three Channel Types in Fabolus

Fabolus makes placing vents and injection ports as easy as clicking on the 3D model. Choose from three channel styles:

<!-- IMAGE_PLACEHOLDER: [Figure 7.2: Three Channel Types in Fabolus. 3D render showing Straight, Angled, and Painted channels with geometric parameters (Tip Diameter, Cone Length, Arc Radius, Channel Diameter) annotated. Dimensions: 900x500px.] -->

### 1. Straight Channel
- **What it is**: A vertical tube running straight up from the bolus surface to the top of the mould.
- **Best for**: 
  - **The Bottom Injection Port**: Placed at the very lowest point of the mould to pump silicone upward.
  - **High Peaks**: Placed at points that point straight up (like the tip of the nose or chin).
- **Settings**: Keep the tip small ($1.5 – 2.5\text{ mm}$) so it snaps off cleanly from the cured silicone, but keep the main tube wide ($4.0 – 5.0\text{ mm}$) for easy flow.

### 2. Angled Channel
- **What it is**: Comes straight out perpendicular from a sloped wall, then curves smoothly upward.
- **Best for**: Steep side walls (like the side of the nose, jawline, or cheek).
- **Why use it**: A straight vertical tube on a steep side wall creates a fragile knife-edge that can break during printing. The angled channel exits cleanly without weak edges.

### 3. Painted (Path) Channel
- **What it is**: A continuous vent slot created by drawing a line with your mouse across the bolus surface.
- **Best for**: Thin curved ridges (like the outer rim of an ear or the bridge of the nose).
- **Why use it**: Instead of clicking a dozen tiny individual vent dots along a curved ear rim, you simply drag your mouse along the ridge to create a continuous vent.

<!-- IMAGE_PLACEHOLDER: [Figure 7.3: Painting a Surface Path Channel on an Auricular Bolus. Viewport screenshot demonstrating the freehand paint tool snapping along the curved rim of an ear bolus, with the live extrusion preview displaying in real time. Dimensions: 900x550px.] -->

---

## How to Place Channels (Step-by-Step)

1. In the **mould** tab, click the **Channels** sub-panel on the left.
2. Select **Straight**, **Angled**, or **Painted**.
3. Move your mouse over the 3D bolus in the viewport. A transparent preview channel follows your cursor, showing you exactly where and how the tube will attach.
4. **Left-click** to place the channel. An anchor sphere marks its location.
5. To change a channel's diameter or delete it, simply click on its anchor sphere and adjust the sliders (or press `Delete` on your keyboard).

---

## The "Bubbles Float Up" Rule (Clinical Checklist)

```
                            Air Vents on High Peaks (Top)
                                  │ │        │ │
                          ┌───────┴─┴────────┴─┴───────┐
                          │       ╭────────────╮       │
                          │       │   CAVITY   │       │
                          │       ╰──────┬─────╯       │
                          └──────────────┼─────────────┘
                                         │
                             Injection Port at Lowest Point (Bottom)
```

1. **Rule 1: Always Inject From the Bottom**
   Place your primary injection port at the lowest point ($Z_{\min}$) of the cavity. Never pour silicone in from the top like pancake batter—that folds air into the mix! Injecting slowly from the bottom pushes a rising wave of silicone upward, sweeping all air out ahead of it.
2. **Rule 2: Put an Air Vent on Every High Point**
   Look at your bolus from all angles. Find every local hill or ridge. If silicone rises past a ridge, any air trapped under that peak will have nowhere to go. Place a vent at every peak!
3. **Rule 3: Use Painted Vents for Ear Rims**
   For delicate, narrow ridges like the outer ear, paint a path along the rim so air escapes evenly along the entire edge.
