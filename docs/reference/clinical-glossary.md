# Clinical & Technical Glossary

This glossary provides authoritative definitions for medical physics, additive manufacturing, materials chemistry, and computational geometry concepts used throughout the **Fabolus** documentation and clinical workflow.

---

## 1. Radiation Oncology & Dosimetry

### Air Gap Underdosage
The clinical phenomenon where microscopic or macroscopic separations between a bolus and the patient's skin allow high-energy photons or electrons to experience renewed electronic buildup in air. Because air has an electron density of $\rho_e \approx 0.0012\text{ g/cm}^3$ compared to tissue ($\rho_e \approx 1.0\text{ g/cm}^3$), secondary electrons scatter out of the field, leading to a local underdosage of $5\%\text{ to }18\%$ at the epidermis. Customized Fabolus boluses eliminate air gaps by conforming directly to patient surface anatomy.

### Bolus
A tissue-equivalent material (physical density $\rho \approx 1.00\text{ to }1.05\text{ g/cm}^3$, effective atomic number $\bar{Z} \approx 7.4$) placed directly on the patient's skin during external beam radiation therapy. Its primary purpose is to shift the depth of maximum dose ($D_{\max}$) toward the surface to treat superficial lesions (e.g., squamous cell carcinoma, melanoma, scar line recurrences) while maintaining uniform dose delivery.

### Depth of Maximum Dose ($D_{\max}$)
The depth along the central beam axis at which absorbed radiation dose reaches $100\%$. For megavoltage photon beams, $D_{\max}$ is governed by forward secondary electron scatter:
- **6 MV photons**: $D_{\max} \approx 1.5\text{ cm}$
- **10 MV photons**: $D_{\max} \approx 2.5\text{ cm}$
- **18 MV photons**: $D_{\max} \approx 3.5\text{ cm}$
Applying a customized bolus of thickness $t_b$ shifts the effective depth such that $D_{\max}$ occurs within or directly adjacent to the skin surface.

### DICOM-RT (RTSTRUCT / RTDOSE / RTPLAN)
The international standard for radiotherapeutic medical imaging data:
- **RTSTRUCT**: Vector contour sets defining anatomical organs-at-risk (OARs), target volumes (GTV, CTV, PTV), and external body contours.
- **RTDOSE**: 3D dose matrices calculated by the TPS.
- **RTPLAN**: Beam angles, collimator settings, multileaf collimator (MLC) leaf sequences, and monitor units (MU).

### Electronic Disequilibrium & Skin Sparing
At the entrance surface of a patient exposed to high-energy photons, high-velocity Compton electrons are projected primarily in the forward direction. Near the skin surface, more electrons leave each unit volume than enter it, producing **electronic disequilibrium**. The resulting entrance dose is relatively low ($15\%\text{ to }30\%$ for 6 MV photons), sparing the epidermis. When superficial tissues are malignant, a bolus restores electronic equilibrium at the skin surface.

### Hounsfield Unit (HU)
A dimensionless unit used in computed tomography (CT) to express radiodensity relative to distilled water:
$$\text{HU} = 1000 \times \frac{\mu - \mu_{\text{water}}}{\mu_{\text{water}} - \mu_{\text{air}}}$$
Standard clinical calibration benchmarks:
- Air: $-1000\text{ HU}$
- Lung: $-700\text{ to }-500\text{ HU}$
- Water: $0\text{ HU}$
- Cured RTV Silicone: $+50\text{ to }+120\text{ HU}$
- Compact Bone: $+1000\text{ to }+3000\text{ HU}$

### Percentage Depth Dose (PDD)
The ratio of absorbed dose at a given depth $d$ along the central axis to the absorbed dose at reference depth $D_{\max}$, expressed as a percentage:
$$\text{PDD}(d) = \frac{D(d)}{D_{\max}} \times 100\%$$

### Treatment Planning System (TPS)
Specialized clinical radiation oncology software (e.g., Varian Eclipse, Elekta Monaco, RaySearch RayStation) used to calculate 3D radiation dose distributions using collapsed cone convolution, pencil beam, or Monte Carlo transport algorithms.

---

## 2. Additive Manufacturing & Casting Chemistry

<!-- IMAGE_PLACEHOLDER: [Figure 17.1: Visual Glossary of Bolus Anatomy and 3D Terminology. Labeled 3D diagram illustrating the anatomical contact surface, bolus solid body, sacrificial mould shell, sprue inlet, riser vents, parting line, and draft angle with callouts explaining each concept.] -->

### Addition-Cure (Platinum-Cured) Silicone
A two-component liquid silicone rubber formulation that cures at room temperature (RTV-2) via a platinum-catalyzed hydrosilylation reaction between vinyl-functional polysiloxanes and silicon-hydride crosslinkers. Characterized by negligible cure shrinkage ($<0.1\%$), excellent skin compatibility, and zero toxic byproducts.

### Cure Inhibition (Catalyst Poisoning)
A chemical failure mode where organotin compounds, amines, sulfur, nitrogen oxides, or condensation-cure residues deactivate the chloroplatinic acid catalyst in platinum-cured silicones. The silicone remains permanently tacky or liquid at the contact interface. Sacrificial PVA or virgin PLA/PETG avoids cure inhibition.

### Degassing
The procedure of placing mixed Part A and Part B silicone resin inside a vacuum chamber at $-29\text{ inHg}$ ($-0.98\text{ bar}$) for 3 to 5 minutes to force micro air bubbles to expand, coalesce, and burst prior to injection.

### Draft Angle ($\alpha$)
The taper angle applied to the vertical walls of a rigid mould cavity to facilitate separation of the cured cast part:
$$\tan\alpha = \frac{\Delta x}{h}$$
For rigid two-part moulds, $\alpha \ge 2^\circ\text{ to }5^\circ$ is standard. For water-soluble sacrificial moulds (PVA), draft angles can be $0^\circ$ because the mould is dissolved rather than pulled.

### Parting Line
The boundary curve along a mould assembly where two or more interlocking mould segments meet. Parting lines must be placed along geometric horizons to eliminate undercuts and prevent silicone flashing into critical dose regions.

### Polyvinyl Alcohol (PVA / PVOH)
A synthetic water-soluble biopolymer used as an FDM 3D printing filament. PVA dissolves completely in warm tap water ($45^\circ\text{C}$), allowing zero-draft, single-piece sacrificial moulds with complex anatomical undercuts to be cleanly removed without tearing fragile silicone features.

### Riser / Air Vent
A narrow auxiliary channel ($\approx 1.5\text{ to }2.0\text{ mm}$ internal diameter) located at the highest gravitational peaks of a mould cavity. Risers allow entrapped air to vent freely as liquid silicone enters from the lowest sprue inlet.

### Shore A Durometer
A standardized measurement scale (ASTM D2240) for elastomeric hardness:
- **Shore 00-10 to 00-30**: Gel-like silicone (cushions, extreme soft tissue).
- **Shore 10A to 20A**: Standard clinical bolus firmness (pliable, conforms to facial contours without compressing tissue).
- **Shore 30A to 50A**: Semi-rigid bolus (extremities, chest wall).

### Sprue
The main injection port (typically $4.0\text{ to }6.0\text{ mm}$ internal diameter with a female Luer-lock or barbed taper) designed into the lowest gravitational point of the sacrificial mould to accept liquid silicone from a dispensing syringe.

### Undercut
A recess, overhang, or protrusion on a 3D part that prevents it from being withdrawn linearly from a single-axis rigid mould without mechanical lock or physical tearing.

---

## 3. Computational Geometry & Mesh Topology

### 2-Manifold Mesh
A triangular surface mesh $\mathcal{M} = (V, E, F)$ where every point on the surface has a local neighborhood topologically homeomorphic to an open 2D disk. Concretely:
1. Every edge $e \in E$ is shared by exactly two triangular faces (or exactly one on an open boundary).
2. The triangles incident to every vertex $v \in V$ form a single continuous fan or open cycle.
3. No self-intersections or intersecting internal faces exist.

### Constructive Solid Geometry (CSG)
A 3D solid modeling technique that combines independent closed volumetric meshes via Boolean algebraic set operations:
- **Union**: $A \cup B$
- **Difference / Subtraction**: $A \setminus B$
- **Intersection**: $A \cap B$

### Euler Characteristic ($\chi$)
A topological invariant defining the structural integrity of a polyhedral mesh:
$$\chi = V - E + F = 2 - 2g - b$$
Where $V$ is vertices, $E$ is edges, $F$ is faces, $g$ is genus (number of through-holes or handles), and $b$ is boundary loops. For a closed, watertight sphere-like mesh ($g=0, b=0$):
$$\chi = V - E + F = 2$$

### Morphological Erosion & Dilation
Non-linear morphological operators applied to meshes:
- **Dilation ($\oplus B_r$)**: Expands the surface outward along surface normals by radius $r$, smoothing out sharp crevices and filling cavities.
- **Erosion ($\ominus B_r$)**: Shrinks the boundary inward along inverted normals by radius $r$, removing tiny protrusions.
The composition $(\mathcal{M} \oplus B_r) \ominus B_r$ forms a morphological closing, eliminating air gaps while maintaining true clinical volume.

### Over-Erosion Guard
A safety invariant in Fabolus that monitors intermediate volume during smoothing and trimming operations. If the current mesh volume $V_{\text{curr}}$ deviates from the initial clinical volume $V_{\text{init}}$ by more than a predefined tolerance:
$$\Delta V = \frac{|V_{\text{curr}} - V_{\text{init}}|}{V_{\text{init}}} > \tau_{\text{volume}} \quad (\tau_{\text{volume}} \approx 1\%)$$
the algorithm halts or clamps the offset distance, preventing clinical under-dosage.

### Parallel Transport Frame (Bishop Frame)
A continuous, rotation-minimizing orthonormal coordinate system $\{ \vec{T}(s), \vec{N}_1(s), \vec{N}_2(s) \}$ constructed along a 3D space curve $\vec{C}(s)$. Unlike standard Frenet-Serret frames, Bishop frames do not suffer from vanishing normal vectors or infinite torsion at inflection points ($\kappa(s) = 0$), ensuring smooth, non-twisting 3D air channels and sprue tubes.

### Signed Distance Field (SDF)
An implicit scalar field $f: \mathbb{R}^3 \to \mathbb{R}$ defined over a volumetric voxel grid, where $f(\vec{x})$ denotes the shortest Euclidean distance from point $\vec{x}$ to the mesh surface $\partial\mathcal{M}$:
$$f(\vec{x}) = \begin{cases} -d(\vec{x}, \partial\mathcal{M}) & \text{if } \vec{x} \text{ is inside } \mathcal{M} \\ 0 & \text{if } \vec{x} \in \partial\mathcal{M} \\ +d(\vec{x}, \partial\mathcal{M}) & \text{if } \vec{x} \text{ is outside } \mathcal{M} \end{cases}$$
SDF representations allow robust level-set offsets and CSG booleans that are completely immune to mesh self-intersections.

### Watertight (Closed) Mesh
A 2-manifold triangle mesh with zero boundary edges ($b=0$). A watertight mesh divides 3D Euclidean space into a strictly bounded interior and an unbounded exterior, allowing unambiguous physical volume calculation and robust slicing.

---

## 4. Fabolus Pipeline & Architectural Concepts

### BaseMesh
The unmodified triangle mesh imported into Fabolus from an external source (such as an STL exported from a clinical TPS). It is stored in metadata (`CoreKeys.BaseMesh`) and serves as the immutable root against which the command pipeline is replayed.

### Command History Pipeline
An ordered, non-destructive list of `IMeshCommand` records stored in a mesh's metadata (`CoreKeys.Commands`). Each command carries a static `Priority` from `CommandPriority`: `Transform = 10` (rotate, translate, smoothing), `TextEmboss = 15`, `Mould = 20`, and `MouldTextEmboss = 25`. Recording a command clears any existing commands with a strictly greater priority (they depended on geometry the new command changed); commands sharing a priority do not clear each other. Replaying the list against the `BaseMesh` reconstructs the current mesh.

### Result / Maybe Pattern (Railway-Oriented Programming)
A functional architecture pattern where operations return `Result<T>` (encapsulating success value or a typed failure diagnostic) or `Maybe<T>` (encapsulating an optional reference without null pointers), ensuring that clinical geometry computations never crash silently or leak corrupted states.

