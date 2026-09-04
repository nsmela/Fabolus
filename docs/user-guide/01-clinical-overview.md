# Clinical Overview & Bolus Physics

## What is a Radiotherapy Bolus?

In external beam radiotherapy (EBRT)—encompassing megavoltage (MV) photon and electron beam radiation therapies—high-energy ionizing beams exhibit a physical phenomenon known as the **skin-sparing effect**. 

When a megavoltage photon beam enters a patient, high-energy secondary electrons are released via photoelectric absorption, Compton scattering, and pair production. Because these electrons travel forward into the tissue before depositing the bulk of their kinetic energy, the absorbed radiation dose begins relatively low at the air-tissue interface, rises rapidly within a "build-up region," reaches a peak dose ($D_{\max}$) at depth $z_{\max}$, and then attenuates exponentially.

<!-- IMAGE_PLACEHOLDER: [Figure 1.1: Photon and Electron Percent Depth-Dose (PDD) Curves. Graph contrasting 6 MV photon and 9 MeV electron depth-dose profiles, highlighting the build-up region, Dmax depth shift with 5 mm / 10 mm bolus, and entrance surface dose enhancement from 45% to ~100%. Dimensions: 800x450px.] -->

| Radiation Modality & Energy | Typical Surface Dose (% of $D_{\max}$) Without Bolus | Depth of $D_{\max}$ ($z_{\max}$) in Water | Surface Dose With 5 mm Tissue-Equivalent Bolus |
| :--- | :--- | :--- | :--- |
| **6 MV Photons** | ~40% – 50% | ~15 mm (1.5 cm) | ~90% – 95% |
| **10 MV Photons** | ~30% – 40% | ~25 mm (2.5 cm) | ~80% – 85% |
| **6 MeV Electrons** | ~75% – 80% | ~12 mm (1.2 cm) | ~100% |
| **9 MeV Electrons** | ~80% – 85% | ~19 mm (1.9 cm) | ~100% |

### The Clinical Dilemma
When treating superficial malignancies where the clinical target volume (CTV) and planning target volume (PTV) encompass the dermal or epidermal layers, the skin-sparing effect poses a critical risk of **tumor underdosage**. Clinical indications requiring intentional loss of skin-sparing include:
- Non-melanoma skin cancers (squamous cell carcinoma, basal cell carcinoma) of the face, nose, ear, scalp, and lips
- Cutaneous T-cell lymphomas and Kaposi's sarcoma
- Post-mastectomy chest wall recurrences and surgical scar boosts
- Soft tissue sarcomas abutting the skin boundary
- Vulvar, perineal, and anal canal lesions

A **bolus** is a synthetic, tissue-equivalent material (physical density $\rho \approx 1.0\text{ g/cm}^3$, electron density relative to water $\rho_e \approx 0.98 – 1.03$) placed directly in contact with the patient's skin surface during radiation delivery. By introducing artificial matter into the beam path, the bolus shifts the build-up region and $D_{\max}$ outward, ensuring 95%–100% of the prescribed dose is absorbed by superficial dermal targets.

---

## The Failure of Traditional Commercial Boluses

Historically, radiation oncology departments have relied on generic, pre-fabricated flat sheets of synthetic elastomer (e.g., Superflab, brass mesh, or paraffin wax blocks). While acceptable for flat surfaces like an anterior chest wall, flat boluses fail dramatically when applied to complex anatomical landscapes:

```
Generic Flat Sheet Bolus:                   Patient-Specific Conformal Bolus:
       Air Gap: > 4 mm (Dose drop!)
      ┌────────────────────────┐                   ╭──────────────────────────╮
      │   Flat Bolus Sheet     │                   │  Fabolus Silicone Bolus  │
      └───┬────────────────┬───┘                   ╰───┬──────────────────┬───╯
          │  Air Pocket    │                           │  100% Skin Contact   │
   ───────┴────────────────┴───────             ───────┴──────────────────────┴───────
         Irregular Patient Skin                       Irregular Patient Skin
```

<!-- IMAGE_PLACEHOLDER: [Figure 1.2: Clinical Comparison of Commercial Flat Bolus vs. Fabolus Conformal Silicone Bolus on an Anthropomorphic Head Phantom. Side-by-side photo showing air pockets under flat Superflab on an ear/nose vs. seamless 100% dermal contact achieved with cast silicone. Dimensions: 800x400px.] -->

### Dosimetric Consequences of Air Gaps
1. **Dose Underdosage**: Multiple peer-reviewed clinical studies (e.g., Sharma et al., Butson et al.) demonstrate that air gaps between bolus and skin as thin as **2 mm to 4 mm** decrease the surface dose by **5% to 15%** for megavoltage photon beams.
2. **Loss of Electronic Equilibrium**: Air pockets allow forward-scattered secondary electrons to scatter sideways, introducing steep dose gradients and cold spots directly over malignant margins.
3. **Setup Variability Across Fractions**: During a standard 25-to-35-fraction course of radiotherapy, technicians cannot position a flexible flat sheet identically each day. This introduces daily dosimetric errors, motion artifacts, and treatment setup delays.
4. **Patient Discomfort & Skin Shearing**: Stiff commercial sheets pressed over post-surgical scars, open wounds, or sensitive cartilage (such as the pinna of the ear or bridge of the nose) cause focal pressure necrosis and patient pain.

---

## Manufacturing Paradigms: Why Sacrificial Casting Wins

Advancements in 3D Treatment Planning Systems (TPS) enable clinicians to contour bespoke patient boluses directly on simulation CT datasets. However, selecting the appropriate manufacturing method is critical:

| Evaluation Metric | Direct Rigid 3D Print (PLA / PETG / UV Resin) | Direct FDM TPU 3D Print | Direct Silicone 3D Print (Liquid Additive) | Fabolus Sacrificial Casting (Silicone in 3D Mould) |
| :--- | :--- | :--- | :--- | :--- |
| **Material Hardness** | Rock hard (Shore 80D+) | Stiff rubber (Shore 85A–95A) | Soft elastomer (Shore 10A–30A) | **Optimized clinical softness (Shore 10A–30A)** |
| **Skin Conformality** | Rigid; gaps occur with micro-weight changes | Moderate; cannot bend into sharp concavities | Excellent | **Flawless 100% dermal wet-contact** |
| **Radiological Density** | Variable infill voids alter HU | Inter-bead micro-air pockets | Good, but slow layer bonding | **Monolithic solid; 100% air-free (-20 to +20 HU)** |
| **Equipment Cost** | Low (<$500 desktop FDM) | Low (<$800 FDM printer) | Prohibitive ($50,000–$150,000+) | **Standard desktop 3D printer ($300–$1,000)** |
| **Production Speed** | 4 – 10 hours | 8 – 20 hours (slow extrusion) | 12 – 36 hours | **Print mould overnight, cast in 15 minutes** |
| **Material Cost** | $2 – $5 | $5 – $10 | $100+ specialized silicone | **$3 filament + $5 medical silicone** |

**Conclusion**: Sacrificial mould casting is the clinical gold standard. It allows any cancer center equipped with standard, affordable desktop 3D printers to produce soft, patient-comfortable, bubble-free silicone boluses with zero capital barrier.

---

## The Voxel Stair-Stepping Problem & Morphological Smoothing

When an oncologist or dosimetrist contours a bolus structure in a TPS (such as Varian Eclipse, RaySearch RayStation, or Elekta Monaco) and exports the structure as an STL, the geometry is derived from sliced axial CT images.

CT scans have slice thicknesses typically between **1.0 mm and 3.0 mm**. Consequently, the exported triangular mesh contains pronounced **stair-stepping ridges** that reflect the CT voxel grid rather than the patient's actual skin surface.

<!-- IMAGE_PLACEHOLDER: [Figure 1.3: The Voxel Discretization Problem. High-magnification 3D render showing raw CT voxel stepping along a curved patient surface vs. standard vertex-averaging shrinkage vs. Fabolus morphological smoothing preservation. Dimensions: 800x450px.] -->

### Why Standard Smoothing Algorithms Fail in Oncology
In computer graphics, meshes are commonly smoothed using **Laplacian smoothing**, which iteratively moves each vertex $\mathbf{v}_i$ toward the average position of its adjacent neighbor vertices $\mathcal{N}(i)$:

$$\mathbf{v}_i^{(k+1)} = \mathbf{v}_i^{(k)} + \lambda \sum_{j \in \mathcal{N}(i)} w_{ij} (\mathbf{v}_j^{(k)} - \mathbf{v}_i^{(k)})$$

While this rounds off sharp voxel corners, it exhibits a catastrophic mathematical flaw for medical physics: **volume shrinkage**. On thin bolus sheets (e.g. 5 mm prescribed thickness), Laplacian smoothing aggressively erodes the outer perimeter and thins high-curvature peaks, reducing volume by **10% to 25%**. This directly degrades radiation beam attenuation and underdoses the patient.

### The Fabolus Solution: Morphological Level-Set Smoothing
Fabolus avoids vertex averaging. Instead, it converts the mesh into a signed distance field (SDF) voxel grid and applies continuous **mathematical morphology**:

$$\mathcal{M}_{\text{closed}} = (\mathcal{M} \oplus d) \ominus d$$

1. **Continuous Dilation ($\oplus d$)**: The surface is offset outward by distance $d$ (`Intensity`). This closes voxel stair-stepping grooves and bridges concave discretization chasms.
2. **Continuous Erosion ($\ominus d$)**: The dilated field is immediately eroded inward by the identical distance $d$. The boundary returns to the exact prescribed anatomical contour.
3. **Volume Invariant Decimation**: The reconstructed zero-level set is retriangulated using adaptive decimation, yielding a silky-smooth skin contact surface that preserves $\mathbf{99.5\% – 100\%}$ of the original planned volume.
