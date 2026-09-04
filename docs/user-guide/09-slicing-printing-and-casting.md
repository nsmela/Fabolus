# Slicing, 3D Printing & Silicone Casting

This guide gives you practical, step-by-step instructions for 3D printing your mould, mixing bubble-free medical silicone, and demoulding a finished patient bolus.

---

## 1. Choosing Your 3D Printing Plastic

<!-- IMAGE_PLACEHOLDER: [Figure 9.1: Slicer Settings for Sacrificial PVA Moulds. Screenshot of Bambu Studio / PrusaSlicer showing 3 perimeters, gyroid infill, and seam placement aligned to external corners. Dimensions: 1000x600px.] -->

| Plastic Type | Best Use Case | Advantages | Disadvantages | Slicer Settings |
| :--- | :--- | :--- | :--- | :--- |
| **PVA (Water-Soluble)** | **Recommended for all complex boluses** (ears, noses, facial contours) | Dissolves 100% in warm water; leaves zero seams or scratches on silicone; zero pulling required. | Absorbs moisture from room air (keep in a sealed dry box); costs ~$60/kg. | Layer: `0.20 mm`, Walls: `3`, Infill: `10% Gyroid` (lets water flow inside to dissolve fast). |
| **Standard PLA** | **Simple flat boluses** or **2-part split moulds** | Inexpensive (~$18/kg); easy to print on any desktop 3D printer. | Does not dissolve; must be peeled away or split into 2 halves. | Layer: `0.20 mm`, Walls: `2` (thin walls for easy peeling), Infill: `0% (Hollow)`. |
| **PETG** | **Reusable moulds for test phantoms** | Extremely durable and washable; silicone won't stick to it. | Very tough; cannot be peeled or broken; must be designed as a 2-part split mould. | Layer: `0.20 mm`, Walls: `4`, Infill: `15% Grid`. |

---

## 2. Slicer Tips for Leak-Proof Moulds

Liquid silicone under pressure will try to seep between 3D printed layers. Use these simple settings in your slicer (Bambu Studio, PrusaSlicer, or Cura) to make sure your mould doesn't leak:

1. **Wall Loops (Perimeters)**: Set to **3 or 4 walls**. Never use a single wall, or tiny pinholes might leak silicone.
2. **Flow Rate (Extrusion Multiplier)**: Bump flow up slightly to **$102\% – 104\%$** for inner walls. This squishes the plastic beads tightly together to make them watertight.
3. **Top and Bottom Layers**: Use at least **4 solid top** and **4 solid bottom** layers.
4. **Print Speed**: Slow down your outer walls to **$30 – 40\text{ mm/s}$**. Slower printing melts the plastic layers together much more solidly.

---

## 3. Important Safety Warning: Silicone Cure Inhibition

> [!CAUTION]
> **CRITICAL RULE: Never Wear Latex Gloves!**
> 
> Most medical-grade silicones (like Smooth-On Dragon Skin or Ecoflex) use a **platinum-cure** formulation. Even the tinest trace of sulfur or contaminants will permanently ruin the chemical reaction, leaving the silicone sticky, gooey, and unusable.
> 
> **Must-Follow Rules:**
> - :x: **NEVER wear latex gloves**. Sulfur in latex ruins silicone instantly.
> - :white_check_mark: **ALWAYS wear powder-free NITRILE gloves**.
> - :x: **Avoid paper cups coated in wax**. Use clean plastic (polypropylene) mixing cups.
> - :x: **Avoid standard UV resin 3D prints**. The chemicals in UV resin poison silicone unless specially sealed.

---

## 4. Mixing, Degassing & Injecting Silicone

<!-- IMAGE_PLACEHOLDER: [Figure 9.2: Vacuum Degassing Chamber Setup. Photo of liquid silicone in a vacuum degassing chamber at -29 inHg showing the frothing expansion phase vs. collapsed bubble-free phase. Dimensions: 800x450px.] -->

### Step 1: Measure and Mix
1. Check your bolus volume in Fabolus (e.g. $80\text{ mL}$).
2. **Mix 20% extra** to account for the syringe and vent tubes ($80\text{ mL} \times 1.20 = 96\text{ mL}$).
3. Weigh equal parts of Part A and Part B (1:1 ratio by weight) into a clean plastic cup.
4. Stir thoroughly for 3 minutes, scraping the sides and bottom of the cup.

### Step 2: Vacuum Degas (Remove All Bubbles)
When you stir silicone, you whip thousands of tiny air bubbles into the mix. Removing them takes just 3 minutes in a vacuum chamber:
1. Place your mixing cup into a vacuum chamber and turn on the pump until the gauge reads **$-29\text{ inHg}$**.
2. The liquid will froth up like boiling foam (it will rise 4 to 5 times its height).
3. Watch through the lid. Once the foam rises and collapses back down, wait another 60 to 90 seconds.
4. Turn off the pump and release the valve. Your silicone is now 100% crystal-clear and bubble-free!

<!-- IMAGE_PLACEHOLDER: [Figure 9.3: Syringe Bottom-Up Injection and Dissolution. Photos showing (A) Luer-lock syringe injecting viscous silicone into bottom sprue until vents overflow, (B) Water bath dissolution of PVA mould, (C) Final pristine silicone bolus. Dimensions: 1000x400px.] -->

### Step 3: Injecting from the Bottom
1. Pour the degassed silicone into a large ($60\text{ mL}$ or $100\text{ mL}$) plastic catheter syringe.
2. Push the syringe tip firmly into the **bottom injection port** of your 3D printed mould.
3. Slowly push the plunger. Watch the silicone rise inside the mould, pushing all air out ahead of it.
4. Keep pushing until solid, bubble-free silicone overflows out of every top vent.
5. Clamp or cap the bottom port with a binder clip.
6. Leave the mould undisturbed at room temperature to cure (typically 4 hours for Dragon Skin 10 NV).

---

## 5. Demoulding & Quality Check (QA)

### Dissolving a PVA Mould:
1. Submerge the cured mould in a container of warm tap water ($40^\circ\text{C} – 45^\circ\text{C}$).
2. *Tip*: An aquarium bubbler or small water pump makes the plastic dissolve in **2 to 3 hours** instead of overnight.
3. As the plastic turns into a soft gel, rinse the silicone bolus clean under running warm water.

### Final Inspection:
1. **Trim Vents**: Snip off the small injection and vent nubs with small curved scissors.
2. **Quick Weight Check**: Weigh the bolus on a kitchen or lab gram scale:
   $$\text{Expected Weight (grams)} \approx \text{Planned Volume (mL)} \times 1.07$$
   (The weight should match within $\pm 2\%$).
3. **CT Scan QA**: Run the bolus through a quick CT scan to verify that there are zero internal air bubbles and uniform soft-tissue density ($0\text{ to }+20\text{ HU}$).
4. **Patient Prep**: Wash with mild soap, wipe with alcohol, and dust lightly with medical cornstarch to make the silicone silky smooth and non-sticky for the patient.

