# Slicing, 3D Printing & Silicone Casting

This guide provides tested, clinical laboratory protocols for 3D printing sacrificial moulds and casting medical-grade silicone boluses.

---

## 1. 3D Printing Material Selection

| Material | Mould Technique | Pros | Cons | Print Considerations |
| :--- | :--- | :--- | :--- | :--- |
| **PVA / PVOH** (Polyvinyl Alcohol) | **Water-Soluble Monolithic Mould** | Completely dissolves in water; zero mechanical strain on soft silicone; no parting lines or flash seams. | Expensive; highly sensitive to ambient humidity; requires active dry box storage. | Print with low moisture, 0.2 mm layer height, 100% infill or 3 solid perimeters to ensure water-tightness. |
| **PLA** (Polylactic Acid) | **Break-Away / Split Mould** | Inexpensive, widely available, rigid, easiest to print cleanly. | Does not dissolve in water; requires 2-part splitting or manual peel-off with snips. | Print with 2 perimeters (thin walls) if peeling off monolithically; standard 0.2 mm layer. |
| **PETG** | **Durable Split Mould** | Excellent chemical resistance, does not stick to silicone, reusable for multiple test castings. | Difficult to break away manually; must be designed as a multi-part mould. | Standard print settings, 15% gyroid infill. |

---

## 2. Recommended Slicer Settings (FDM / FFF)

When importing the exported mould STL or 3MF into your slicer (e.g. Bambu Studio, PrusaSlicer, Cura):

1. **Perimeter Walls**: Set to **3 perimeters** (approx. 1.2–1.5 mm wall thickness). This prevents uncured liquid silicone from seeping through micro-porosities between extrusion tracks.
2. **Top / Bottom Solid Layers**: Minimum **4 layers** (0.8–1.0 mm) to guarantee leak-tight sealing at the bottom plate.
3. **Infill**: 
   - For PVA soluble moulds: **10–15% Gyroid or Grid infill** (allows water to penetrate into internal channels rapidly during dissolution).
   - For PLA peel-away moulds: **0% infill (hollow) with 2 perimeters** allows the shell to crush and peel off like an eggshell.
4. **Seam Placement**: Set seam placement to **Random** or align it along the external shell corners to prevent vertical ridge artifacts inside the casting cavity.

---

## 3. Liquid Silicone Preparation & Vacuum Degassing

### Recommended Silicone Systems
- **Smooth-On Dragon Skin 10 NV / 20 / 30**: Addition-cure (platinum) silicone. Shore 10A–30A closely replicates human soft tissue.
- **Smooth-On Ecoflex 00-30**: Extremely soft, ideal for ultra-sensitive skin or mucosal contact.

### Protocol:
1. **Mix**: Weigh Part A and Part B according to the manufacturer's ratio (typically 1:1 by weight) in a clean polypropylene mixing container. Stir thoroughly for 3 minutes, scraping walls and bottom.
2. **Vacuum Degassing (Crucial Step)**:
   - Place the mixed silicone in a vacuum degassing chamber at $-29\text{ inHg}$ (approx. $1\text{ bar}$ vacuum).
   - The mixture will froth and expand up to 4–5 times its volume as trapped air boils out.
   - Hold vacuum until the mixture collapses completely, then hold for an additional 2 minutes.
   - Release vacuum slowly to collapse remaining surface micro-bubbles.

---

## 4. Injection & Curing

```
              Vent Chimneys (Open)
                  │ │      │ │
             ┌────┴─┴──────┴─┴────┐
             │     ╭────────╮     │
             │     │ Cavity │     │
             │     ╰───┬────╯     │
             └─────────┼──────────┘
                       │
             ┌─────────┴──────────┐
             │ Luer-Lock Syringe  │  <── Push steadily from bottom
             └────────────────────┘
```

1. **Syringe Loading**: Draw the degassed silicone into a 60 mL or 100 mL catheter-tip or Luer-lock syringe.
2. **Bottom-Up Injection**:
   - Mate the syringe tip snugly to the lowest injection sprue at the base of the mould.
   - Depress the plunger slowly and continuously.
   - Observe the silicone rising inside the cavity through the translucent mould walls.
   - Continue injecting until liquid silicone rises cleanly through all top-mounted air vents and overflow risers with zero bubbling.
3. **Sealing & Curing**:
   - Cap or tape off the injection sprue and let the mould sit undisturbed at room temperature ($21^\circ\text{C} – 23^\circ\text{C}$) for the full cure duration (typically 4–16 hours depending on silicone grade).

---

## 5. Demoulding & Cleaning

### For Water-Soluble PVA Moulds:
1. Submerge the cured mould in a warm water bath ($40^\circ\text{C} – 50^\circ\text{C}$).
2. An aquarium pump or ultrasonic cleaner drastically accelerates dissolution (typically 2–4 hours).
3. Once the PVA dissolves into a non-toxic sludge, rinse the silicone bolus thoroughly under warm running water.

### For Split / Peel-Away PLA Moulds:
1. Remove all external clamps and pry halves apart along the parting line.
2. If peeling a single-wall shell, use flush snips to notch the top edge and peel the plastic away in strips like an orange peel.

### Post-Processing:
- Trim all sprue and riser nubs flush using curved iris scissors.
- Wash the bolus with mild medical-grade antibacterial soap and isopropyl alcohol (70% IPA).
- Dust lightly with medical-grade cornstarch or cosmetic powder to eliminate surface tackiness before patient fitting.
