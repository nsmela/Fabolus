# Clinical Overview & Bolus Physics

## What is a Radiotherapy Bolus?

In external beam radiotherapy (EBRT)—including photon and electron beam therapies—high-energy ionizing radiation exhibits a physical phenomenon known as **skin sparing**. The absorbed radiation dose starts low at the patient's skin surface, reaches a peak ($D_{\max}$) at a depth determined by the beam energy (e.g., ~1.5 cm for a 6 MV photon beam), and then gradually falls off.

However, when treating superficial malignancies such as:
- Non-melanoma skin cancer (squamous cell carcinoma, basal cell carcinoma) of the nose, ear, scalp, or face
- Cutaneous metastases
- Chest wall recurrences post-mastectomy
- Sarcomas abutting the dermis
- Vulvar / perineal tumors

The target volume (PTV) encompasses the skin surface itself. Without intervention, skin-sparing would critically underdose the superficial tumor tissue.

A **bolus** is a tissue-equivalent material (electron density $\rho_e \approx 1.0\text{ g/cm}^3$) placed directly in contact with the patient's skin during treatment. It acts as artificial tissue, shifting $D_{\max}$ anteriorly into the target volume to guarantee adequate therapeutic dose delivery to the superficial dermis.

---

## The Challenge with Standard Commercial Boluses

Traditional clinical departments rely on flat sheets of commercial elastomeric bolus (e.g., Superflab). When applied to complex anatomical contours (scalp, nose, outer ear, neck folds):
1. **Air Gaps**: The flat sheet cannot conform to concave geometries. Air gaps as small as **2–4 mm** reduce surface dose significantly (up to 10–15% loss) and introduce unpredictable dosimetry due to loss of electronic equilibrium.
2. **Patient Discomfort & Reproducibility**: Rigid or poorly fitting boluses cause pressure points, patient motion, and inconsistent day-to-day setup reproducibility across 20–35 daily radiation fractions.

---

## Patient-Specific 3D-Printed Bolus: Direct Printing vs. Sacrificial Moulding

With modern Treatment Planning Systems (TPS) and CT segmentation, clinicians can contour custom boluses conforming perfectly to the patient's anatomy. However, manufacturing presents a dilemma:

| Fabrication Method | Advantages | Clinical Disadvantages |
| :--- | :--- | :--- |
| **Direct Rigid 3D Printing (PLA / PETG / Resin)** | Fast, single-step print | Rigid, unyielding, painful against sensitive or post-surgical skin. Small contour mismatches cause severe air gaps. |
| **Direct Soft 3D Printing (FDM TPU)** | Moderately flexible | High durometer (Shore 85A–95A is still stiff), print seams harbor air pockets, unpredictable radiological density. |
| **Direct Silicone 3D Printing** | Soft, biocompatible | Extremely expensive machinery (specialized liquid additive manufacturing), slow print speeds, limited availability. |
| **Fabolus Sacrificial Moulding (Silicone Casting)** | **Clinical Gold Standard**: Uses medical-grade liquid silicone (Shore 10A–30A, matching soft human tissue), completely bubble-free, perfectly conformal, negligible cost, highly reproducible. | Requires mould generation, degassing, and injection workflow. |

**Fabolus automates and streamlines the sacrificial mould approach**, removing the CAD complexity so clinical teams can generate casting moulds and vent channels in minutes.

---

## Why Morphological Smoothing Matters

When bolus structures are exported from a TPS (via DICOM RT Structure converted to STL), they suffer from **voxel stair-stepping** caused by axial CT slice thicknesses (typically 1.5–3.0 mm).

```
CT Voxelized Surface:       Naive Smoothing (Shrinkage):    Fabolus Morphological (Volume Preserved):
      ┌──┐                          ╭──────╮                          ╭──────────╮
   ┌──┘  └──┐                      │        │                        │            │
┌──┘        └──┐                  ╰────────╯                        ╰────────────╯
(Stair-stepping)             (Dose calculation invalid)          (Smooth skin contact, correct volume)
```

- **Naive Laplacian Smoothing**: Repeatedly averages vertex positions with their neighbors. This rapidly erodes high-curvature features, shrinks total volume, and thins the bolus, invalidating the planned radiation dose distribution.
- **Fabolus Morphological Smoothing**: Uses a volumetric Erosion-Dilation-Resize pipeline (`MR.doubleOffsetMesh` via MeshInspector's native core). It smooths out high-frequency voxel noise while strictly preserving net volume and prescription thickness.
