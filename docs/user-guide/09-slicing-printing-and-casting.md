# Slicing, 3D Printing & Silicone Casting

This manual provides clinical-grade, laboratory-tested protocols for slicing sacrificial moulds, 3D printing with water-soluble and rigid polymers, and casting bubble-free, patient-specific silicone boluses.

---

## 1. 3D Printing Polymers & Chemical Compatibility

<!-- IMAGE_PLACEHOLDER: [Figure 9.1: Slicer Settings for Sacrificial PVA Moulds. Screenshot of Bambu Studio / PrusaSlicer showing 3 perimeters, gyroid infill, and seam placement aligned to external corners. Dimensions: 1000x600px.] -->

| Polymer | Recommended Clinical Application | Pros | Cons | Slicer Configuration |
| :--- | :--- | :--- | :--- | :--- |
| **PVA / PVOH** (Polyvinyl Alcohol) | **Water-Soluble Monolithic Moulds** (Standard for complex ear, nose, face boluses) | Dissolves 100% in warm water; zero demoulding strain on delicate silicone; no parting seams or flash lines. | Sensitive to ambient humidity; requires dry box printing; higher material cost (~$60–$90/kg). | Layer: `0.20 mm`, Walls: `3 perimeters`, Infill: `10% Gyroid` (channels water inside for rapid dissolution), Temp: `205°C` nozzle / `55°C` bed. |
| **Standard PLA** (Polylactic Acid) | **Crush-and-Peel Moulds** or **2-Part Split Moulds** | Inexpensive (~$18/kg); rigid; prints with zero warping; universally available. | Insoluble; monolithic moulds must be peeled manually with flush cutters; potential micro-scratches. | Layer: `0.20 mm`, Walls: `2 perimeters` (thin shell for easy crushing), Infill: `0% (Hollow)`. |
| **PETG** | **Reusable 2-Part Clamped Moulds** (QA test phantoms) | Extreme chemical inertness; silicone will not adhere even without release agents; washable. | Highly ductile; cannot be crushed or peeled; strictly requires 2-part split design. | Layer: `0.20 mm`, Walls: `4 perimeters`, Infill: `15% Grid`, Temp: `240°C` nozzle / `75°C` bed. |

---

## 2. Advanced Slicer Configuration for Leak-Proof Moulds

Liquid silicone under syringe injection pressure will weep through microscopic gaps between 3D print layer beads. Follow these strict slicer parameters in Bambu Studio, PrusaSlicer, or Cura:

1. **Perimeter Count (Wall Loops)**: Set to **minimum 3 perimeters** (approx. $1.2 – 1.5\text{ mm}$ wall thickness). Never use a single perimeter; pinhole voids between extrusion tracks will leak silicone.
2. **Extrusion Multiplier (Flow Rate)**: Calibrate flow to **$1.02 – 1.04$ ($102\% – 104\%$)** for the inner cavity perimeters. Slight over-extrusion fuses adjacent beads into a hydraulically sealed, non-porous face.
3. **Top and Bottom Solid Shells**: Minimum **4 solid bottom layers** and **4 solid top layers** ($0.8 – 1.0\text{ mm}$).
4. **Seam Alignment**: Set to **Rear** or **Random**. Avoid placing the Z-seam along internal cavity corners to prevent vertical ridge transfer onto the silicone contact surface.
5. **Print Speed**: Slow down outer wall speeds to **$30 – 40\text{ mm/s}$**. Slower speeds dramatically increase inter-layer thermal bonding strength.

---

## 3. Medical Silicone Chemistry & Cure Inhibition Warning

> [!CAUTION]
> **CRITICAL CLINICAL ALERT: Platinum Cure Inhibition**
> 
> Most medical-grade silicone elastomers (e.g. Smooth-On Dragon Skin, Ecoflex) use a **platinum-catalyzed addition cure** mechanism. The platinum catalyst is poisoned by minute traces of contaminants, leaving the silicone permanently sticky, unpolymerized, and toxic against patient skin.
> 
> **STRICT PROTOCOLS:**
> - **NEVER wear latex gloves**. Natural latex contains sulfur compounds that instantly inhibit platinum cure. **Wear ONLY powder-free NITRILE gloves**.
> - **Avoid UV-curing photopolymer resins**. Liquid SLA/DLP resins contain amines and photoinitiators that poison silicone. If using an SLA mould, it must be post-cured at 60°C and coated with Inhibit X.
> - **Use clean polypropylene (PP) or polyethylene (PE) mixing cups**. Never use paper cups coated in wax.

---

## 4. Degassing & Injection Protocol

<!-- IMAGE_PLACEHOLDER: [Figure 9.2: Vacuum Degassing Chamber Setup. Photo of liquid silicone in a vacuum degassing chamber at -29 inHg showing the frothing expansion phase vs. collapsed bubble-free phase. Dimensions: 800x450px.] -->

### Phase A: Proportioning & Vacuum Degassing
1. Calculate the required silicone volume:
   $$V_{\text{prep}} = V_{\text{bolus}} \times 1.20$$
   *(The extra 20% accounts for syringe dead space, injection sprues, and vent risers).*
2. Using an analytical gram balance, weigh Part A and Part B (1:1 ratio by weight) into a clean polypropylene beaker.
3. Stir deliberately for 3 minutes using a flat-edged spatula, scraping the bottom and sides.
4. Place the beaker into a vacuum degassing chamber. Pull vacuum to **$-29\text{ inHg}$ ($~1\text{ bar}$)**.
5. The mixture will froth and rise to 4–5 times its original volume. Hold until the froth crest collapses completely, then maintain deep vacuum for an additional 90 seconds.
6. Slowly vent the chamber to atmospheric pressure. The liquid is now 100% bubble-free.

<!-- IMAGE_PLACEHOLDER: [Figure 9.3: Syringe Bottom-Up Injection and Dissolution. Photos showing (A) Luer-lock syringe injecting viscous silicone into bottom sprue until vents overflow, (B) Water bath dissolution of PVA mould, (C) Final pristine silicone bolus. Dimensions: 1000x400px.] -->

### Phase B: Bottom-Up Syringe Injection
1. Draw the degassed silicone into a 60 mL or 100 mL Luer-lock catheter syringe.
2. Dock the syringe tip firmly into the lowest injection sprue at the base of the printed mould.
3. Depress the syringe plunger slowly and steadily.
4. Watch through the translucent mould walls as the liquid silicone rises uniformly from bottom to top, displacing air ahead of the liquid front.
5. Continue injecting until solid, bubble-free silicone emerges cleanly from every top-mounted vent chimney.
6. Cap or pinch the injection sprue with a plastic clamp. Let the assembly cure at room temperature ($21^\circ\text{C} – 23^\circ\text{C}$) for the manufacturer-specified cure time (typically 4–5 hours for Dragon Skin 10 NV; 16 hours for Dragon Skin 30).

---

## 5. Demoulding & Clinical Quality Assurance (QA)

### Dissolution of PVA Sacrificial Moulds:
1. Immerse the cured mould in a warm water bath ($40^\circ\text{C} – 45^\circ\text{C}$).
2. An aquarium circulation pump or heated ultrasonic cleaning tank accelerates dissolution from 8 hours down to **2 to 3 hours**.
3. As the PVA turns into a soft gelatinous slurry, peel away any thick remnants and wash the silicone bolus under warm running tap water.

### Final QA Verification:
- **Sprue Trimming**: Use fine curved iris scissors to flush-trim all injection sprue and vent nubs.
- **Physical Weight Verification**: Weigh the finished silicone bolus on a calibrated laboratory scale:
  $$M_{\text{measured}} \approx V_{\text{planned}} \times \rho_{\text{silicone}} \quad (\pm 2\%)$$
  *(Dragon Skin density $\rho \approx 1.07\text{ g/cm}^3$; Ecoflex $\rho \approx 1.04\text{ g/cm}^3$).*
- **CT QA Verification**: Scan the bolus on the simulation CT scanner. Verify that Hounsfield Units are uniform ($-20\text{ to }+20\text{ HU}$) and that zero internal air voids are visible on axial slices.
- **Sanitization**: Wash with medical antibacterial soap, wipe with 70% Isopropyl Alcohol (IPA), and dust lightly with pharmaceutical cornstarch to eliminate surface tackiness before patient fitting.
