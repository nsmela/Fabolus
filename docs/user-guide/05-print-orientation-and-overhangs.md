# Print Orientation & Overhangs

3D printers build parts layer-by-layer from the bottom up. If a surface slopes too steeply downward into mid-air, the printer cannot place hot plastic without it drooping. 

In Fabolus, orienting your bolus properly ensures your mould prints quickly, cleanly, and without messy supports inside the mould cavity.

---

## Why Orientation Matters for Patient Boluses

In normal 3D printing, support columns can simply be snapped off and sanded smooth. But with bolus moulds, the inside of the mould directly forms the surface that touches the patient's skin:
1. **No Supports Inside the Cavity**: If supports print inside the hollow mould cavity, they are nearly impossible to clean out.
2. **Protecting Irradiated Skin**: Any rough plastic bumps left inside the mould will imprint onto the silicone, turning into scratchy, sandpaper-like spots that irritate sensitive patient skin.
3. **Preventing Air Pockets**: Rough surfaces inside the mould trap air bubbles as liquid silicone rises, creating voids in the bolus.

<!-- IMAGE_PLACEHOLDER: [Figure 5.1: Overhang Physics in Additive Manufacturing. Diagram illustrating layer deposition angle relative to the print bed, showing stable layer overlap at 45° vs drooping filament at 70° without supports. Dimensions: 800x400px.] -->

---

## The Traffic Light Color Guide

When you click the **rotate** tab, Fabolus automatically colors your 3D model like a traffic light to show how easy each surface will be to print:

```
🟢 GREEN (0° to 45°)   ── Easy to print. Supports are not needed.
🟡 YELLOW (45° to 65°) ── Moderate slope. Prints well on most modern 3D printers.
🔴 RED (Over 65°)      ── Steep cliff / ceiling. Molten plastic will droop without supports.
```

<!-- IMAGE_PLACEHOLDER: [Figure 5.2: The Rotate Tool Interface. Screenshot showing the 3-axis rotation gizmo encircling the bolus in the SharpDX viewport, with the degree sliders (X, Y, Z) and Warning/Critical angle adjustment controls highlighted in the left panel. Dimensions: 900x550px.] -->

### Using the Rotation Rings
- Click and drag the **Red**, **Green**, or **Blue** rings around your model to turn it in 3D space.
- The colors update instantly as you rotate, letting you see in real time when you reach an ideal position.
- You can also type exact angles into the $X, Y, Z$ boxes on the left, or click **Reset** to return to the original position.

---

## The Three Golden Rules of Bolus Orientation

<!-- IMAGE_PLACEHOLDER: [Figure 5.3: Orientation Comparison for an Ear Bolus. Side-by-side comparison: Suboptimal orientation requiring internal cavity supports vs. Optimized orientation where the skin-contact face points upward, placing all support attachments harmlessly on the exterior mould shell. Dimensions: 1000x500px.] -->

### Rule 1: Point the Skin-Contact Side UP
Always rotate the bolus so that the hollow, skin-contacting side faces **upward**.
- **Why**: When the skin-contact face points up, the 3D printer finishes it as smooth, clean top layers.
- **Bonus**: Any support structures will only attach to the *outside* of the disposable mould shell—which gets dissolved or thrown away anyway!

### Rule 2: Lay the Bolus as Flat as Possible
Align the longest side of the bolus flat against the print bed.
- **Why**: Shorter prints finish hours faster, wobble less during printing, and are much less likely to peel off the print bed.

### Rule 3: Keep the High Point at the Top
Make sure the tallest peak of the bolus points straight up.
- **Why**: When you inject liquid silicone later, air bubbles naturally float straight up to the highest point. Having a single peak at the top makes it easy to vent out all the air with a single vent channel.

