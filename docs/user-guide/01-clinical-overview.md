# Clinical Overview & Bolus Physics

## What is a Radiotherapy Bolus?

When doctors use high-energy radiation beams (like 6 MV X-rays or electrons) to treat cancer, the beams naturally deposit very little dose right at the skin surface. Instead, the radiation penetrates a short distance into tissue before reaching its peak strength—a natural protective feature known as the **skin-sparing effect**.

While skin-sparing is great for protecting healthy skin when treating deep organs (like the prostate or lung), it creates a serious problem when treating **cancers on or just under the skin**:
- Skin cancers on the nose, ear, scalp, or lips
- Surgical scar lines and chest wall recurrences
- Shallow tumors where the target reaches the surface

Without help, surface cancer cells receive only 30% to 50% of the prescribed radiation dose, risking cancer recurrence.

<!-- IMAGE_PLACEHOLDER: [Figure 1.1: Photon and Electron Percent Depth-Dose (PDD) Curves. Graph contrasting 6 MV photon and 9 MeV electron depth-dose profiles, highlighting the build-up region, Dmax depth shift with 5 mm / 10 mm bolus, and entrance surface dose enhancement from 45% to ~100%. Dimensions: 800x450px.] -->

| Beam Energy | Skin Surface Dose Without Bolus | Depth of Maximum Dose ($D_{\max}$) | Surface Dose With a 5 mm Bolus |
| :--- | :--- | :--- | :--- |
| **6 MV Photons (Standard X-ray)** | ~40% – 50% | ~15 mm (1.5 cm deep) | **~90% – 95% (Effective)** |
| **10 MV Photons** | ~30% – 40% | ~25 mm (2.5 cm deep) | **~80% – 85%** |
| **6 MeV Electrons** | ~75% – 80% | ~12 mm (1.2 cm deep) | **~100% (Full dose)** |
| **9 MeV Electrons** | ~80% – 85% | ~19 mm (1.9 cm deep) | **~100% (Full dose)** |

### How a Bolus Solves This
A **bolus** is a soft, tissue-like material placed directly against the patient's skin during treatment. It tricks the radiation beam into thinking the patient's body starts earlier. By passing through the bolus first, the beam reaches full therapeutic strength right as it touches the patient's skin.

---

## Why Traditional Flat Boluses Fail

For decades, clinics used generic, flat rubber sheets (such as Superflab or wax blocks). While flat sheets work fine on flat areas like the abdomen, they fail on curved anatomy:

```
Traditional Flat Sheet:                         Fabolus Custom Silicone Bolus:
       Air Gaps Cause Dose Drop!
      ┌────────────────────────┐                       ╭──────────────────────────╮
      │    Flat Rubber Sheet   │                       │  Fabolus Silicone Bolus  │
      └───┬────────────────┬───┘                       ╰───┬──────────────────┬───╯
          │   Air Pocket   │                               │    Zero Air Gaps     │
   ───────┴────────────────┴───────                 ───────┴──────────────────────┴───────
         Curved Patient Skin                              Curved Patient Skin
```

<!-- IMAGE_PLACEHOLDER: [Figure 1.2: Clinical Comparison of Commercial Flat Bolus vs. Fabolus Conformal Silicone Bolus on an Anthropomorphic Head Phantom. Side-by-side photo showing air pockets under flat Superflab on an ear/nose vs. seamless 100% dermal contact achieved with cast silicone. Dimensions: 800x400px.] -->

### The Dangers of Air Pockets
1. **Underdosing the Tumor**: Even a tiny air gap of **2 mm to 4 mm** drops the skin dose by **5% to 15%**. This allows microscopic tumor cells to receive less radiation than planned.
2. **Setup Inconsistency**: A flexible flat sheet wrinkles differently every single day across a 30-day treatment course, making treatments inconsistent.
3. **Patient Discomfort**: Stiff flat sheets pressed over ears, noses, or fresh surgical scars cause painful pressure points and skin irritation.

---

## Why 3D Printed Moulds & Cast Silicone Win

Modern treatment planning software allows clinics to design a custom 3D bolus for each patient. But how should you manufacture it?

| Method | Comfort & Softness | Skin Fit | Bubble-Free Density | Equipment Cost | Production Time |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Rigid 3D Print (PLA / Resin)** | Rock hard; painful for patients | Poor; leaves gaps if patient moves | Inconsistent | Low (<$500) | 4 – 10 hours |
| **Flexible Filament 3D Print (TPU)** | Stiff rubber; hard to bend | Fair; struggles on tight curves | Micro air pockets between layers | Low (<$800) | 8 – 20 hours |
| **Direct Silicone 3D Printer** | Soft and flexible | Excellent | Good | Prohibitive ($50k–$150k+) | 12 – 36 hours |
| **Fabolus Sacrificial Mould** | **Very soft & comfortable (Shore 15A–20A)** | **Flawless 100% skin contact** | **100% Solid & Bubble-Free** | **Standard Desktop 3D Printer ($300–$800)** | **Print mould overnight, cast in 15 min** |

**The Fabolus Approach**: You 3D print a disposable negative mould (often in water-soluble PVA filament), pour or inject liquid medical silicone, let it cure, and wash the mould away in warm water. The result is a soft, perfectly fitting silicone bolus for around **$10 in materials**.

---

## The "Staircase" Problem and Smart Smoothing

When you export a bolus from your Treatment Planning System (TPS), it is built from stacked 2D CT slices (usually spaced 1.5 mm to 3 mm apart). This creates a 3D model with rough, jagged **stair-stepping ridges**.

<!-- IMAGE_PLACEHOLDER: [Figure 1.3: The Voxel Discretization Problem. High-magnification 3D render showing raw CT voxel stepping along a curved patient surface vs. standard vertex-averaging shrinkage vs. Fabolus morphological smoothing preservation. Dimensions: 800x450px.] -->

### Why Ordinary 3D Smoothing Destroys Boluses
Most 3D graphics software uses simple "vertex averaging" to smooth models. While this rounds off sharp bumps, it shrinks the shape like a melting ice cube. On a thin 5 mm bolus, ordinary smoothing can shrink the volume by **15% to 25%**, making the bolus too thin to deliver the prescribed radiation dose!

### How Fabolus Solves This
Fabolus uses **volume-preserving smoothing**:
1. It gently expands the surface outward just enough to fill in the jagged CT slice grooves.
2. It contracts the surface back inward by the exact same distance to return the bolus to its true anatomical contour.
3. The result is a smooth, comfortable surface that matches the patient's skin while preserving **over 99% of the prescribed thickness and volume**.
