# Clinical & Technical Glossary

## Radiation Therapy Terms

- **Bolus**: A tissue-equivalent material (electron density $\approx 1.0\text{ g/cm}^3$) placed directly over a patient's skin during radiation therapy to eliminate skin-sparing effect and build up dose at the skin surface.
- **Dmax ($D_{\max}$)**: Depth of maximum dose in tissue. For high-energy photons (e.g. 6 MV), $D_{\max}$ occurs approximately 1.5 cm below the surface. A bolus shifts $D_{\max}$ anteriorly into superficial tumors.
- **EBRT**: External Beam Radiotherapy. Delivery of high-energy ionizing radiation from an external linear accelerator (LINAC).
- **Skin Sparing**: The physical effect whereby megavoltage photon beams deliver a relatively low entrance dose to the epidermis due to electronic disequilibrium, protecting normal skin when deep lesions are targeted.
- **TPS**: Treatment Planning System (e.g. Varian Eclipse, Elekta Monaco, RaySearch RayStation). Software used by medical physicists to calculate radiation dose distributions on patient CT scans.
- **DICOM-RT**: Digital Imaging and Communications in Medicine - Radiation Therapy. Standard medical data format containing CT imagery, contoured structure sets (RTSTRUCT), and dose plans (RTDOSE).
- **HU (Hounsfield Unit)**: Radiodensity scale in CT imaging. Air is $-1000\text{ HU}$, Water is $0\text{ HU}$, and soft tissue is typically $+20\text{ to }+50\text{ HU}$.

---

## 3D Printing & Casting Terms

- **Sacrificial Mould**: A temporary mould printed in water-soluble or easily peeled material used to cast liquid silicone, which is destroyed during demoulding.
- **PVA / PVOH (Polyvinyl Alcohol)**: A synthetic polymer soluble in warm water, ideal for single-piece sacrificial moulds with deep undercuts.
- **Sprue**: The primary passage or port through which liquid silicone is injected into the casting cavity.
- **Riser / Vent**: A secondary channel allowing trapped air and excess silicone to escape as the cavity fills from below.
- **Degassing**: The process of placing mixed liquid silicone under deep vacuum ($-29\text{ inHg}$) to extract entrapped air bubbles prior to injection.
- **Shore Durometer**: A standardized measure of material hardness. Soft medical silicone boluses typically range from Shore 10A (very soft) to Shore 30A (moderately firm).

---

## Computational Geometry Terms

- **2-Manifold**: A 3D surface where every point has a local neighborhood topologically equivalent to a 2D disk. Every edge must connect exactly two triangles.
- **Watertight**: A closed, continuous manifold surface with no open boundary edges (holes), enclosing a well-defined solid volume.
- **CSG (Constructive Solid Geometry)**: Solid modeling technique combining primitive solids via Boolean operations (Union, Subtraction, Intersection).
- **Erosion & Dilation**: Morphological filtering operations. Dilation expands a boundary outward; erosion contracts it inward.
- **SDF (Signed Distance Field)**: A volumetric representation where every point in space stores its shortest Euclidean distance to the mesh surface, negative inside and positive outside.
