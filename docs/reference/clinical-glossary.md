# Clinical & Technical Glossary

This glossary provides clear, practical definitions for medical physics, additive manufacturing, materials chemistry, and 3D geometry concepts used throughout the **Fabolus** documentation and clinical workflow.

---

## 1. Radiation Oncology & Dosimetry

### Air Gap Underdosage
The reduction in radiation dose that occurs when gaps or pockets of air exist between a bolus and the patient's skin. Because air is roughly 800 times less dense than human tissue, secondary radiation particles scatter out of the field instead of depositing energy, causing an underdosage of 5% to 18% at the skin surface where treatment is needed. Custom-fitted Fabolus boluses eliminate air gaps by conforming directly to the patient's anatomy.

### Bolus
A soft, flexible, tissue-equivalent material (such as medical-grade silicone) placed directly on the patient's skin during radiation therapy. Its primary purpose is to bring the point of maximum radiation dose up to the skin surface to properly treat superficial lesions (such as skin cancer or scar recurrences) while maintaining uniform dose delivery.

### Depth of Maximum Dose (Dmax)
The depth beneath the skin where absorbed radiation dose reaches 100%. For high-energy therapeutic photon beams, this peak dose naturally occurs several millimeters to centimeters below bare skin:
- **6 MV photons**: reaches maximum dose approximately 1.5 cm deep
- **10 MV photons**: reaches maximum dose approximately 2.5 cm deep
- **18 MV photons**: reaches maximum dose approximately 3.5 cm deep

Placing a custom bolus over the skin shifts this peak dose point forward onto the surface where superficial cancer cells reside.

### DICOM-RT (RTSTRUCT / RTDOSE / RTPLAN)
The international medical imaging standard for radiotherapeutic data:
- **RTSTRUCT**: 3D outlines and contours defining patient organs-at-risk (OARs), target volumes (GTV, CTV, PTV), and external body contours.
- **RTDOSE**: 3D radiation dose matrices calculated by the treatment planning system.
- **RTPLAN**: Machine settings including beam angles, collimator positions, and radiation monitor units (MU).

### Electronic Disequilibrium & Skin Sparing
When high-energy radiation beams enter tissue, radiation particles knock electrons forward into deeper tissue. Right at the entrance surface, fewer electrons have had a chance to deposit energy, resulting in a naturally lower dose (15% to 30% for standard 6 MV beams) that spares normal skin. When treating surface tumors, this natural skin-sparing effect leaves cancer cells undertreated. Placing a custom bolus on the skin restores full electronic equilibrium, delivering 100% dose directly to the skin surface.

### Hounsfield Unit (HU)
A standardized scale used in computed tomography (CT) scans to measure material density compared to water:
- **Air**: -1,000 HU
- **Lung**: -700 to -500 HU
- **Water**: 0 HU
- **Cured RTV Silicone Bolus**: +50 to +120 HU (closely matching human soft tissue)
- **Dense Bone**: +1,000 to +3,000 HU

### Percentage Depth Dose (PDD)
The percentage of radiation dose delivered at a specific depth compared to the point of maximum dose (Dmax). Medical physicists use PDD measurements to evaluate radiation penetration and confirm the exact bolus thickness required for clinical efficacy.

### Treatment Planning System (TPS)
Specialized clinical radiation oncology software (such as Varian Eclipse, Elekta Monaco, or RaySearch RayStation) used to calculate 3D radiation dose distributions and plan patient treatments.

---

## 2. Additive Manufacturing & Casting Chemistry

<!-- IMAGE_PLACEHOLDER: [Figure 17.1: Visual Glossary of Bolus Anatomy and 3D Terminology. Labeled 3D diagram illustrating the anatomical contact surface, bolus solid body, sacrificial mould shell, sprue inlet, riser vents, parting line, and draft angle with callouts explaining each concept.] -->

### Addition-Cure (Platinum-Cured) Silicone
A high-grade, two-part liquid silicone rubber that cures at room temperature. It features virtually zero cure shrinkage (under 0.1%), excellent skin safety, and high tear resistance, making it the preferred material for patient-contact boluses.

### Cure Inhibition (Catalyst Poisoning)
A chemical issue where contaminants—such as sulfur, latex, certain resins, or condensation-cure residues—deactivate the platinum catalyst in liquid silicone. The silicone remains permanently tacky or liquid at the contact interface. Using clean PVA, PLA, or PETG moulds prevents inhibition.

### Degassing
The procedure of placing mixed Part A and Part B liquid silicone inside a vacuum chamber for 3 to 5 minutes before injection. The vacuum forces microscopic air bubbles to expand and burst, preventing internal voids that could alter radiation absorption.

### Draft Angle
The slight taper or slant applied to the vertical walls of a rigid mould cavity so the cured cast part can be separated easily without sticking. Sacrificial moulds that dissolve in water or split open easily do not require draft angles (0° draft is fine) because the mould is washed away or opened rather than pulled off.

### Parting Line
The boundary seam where two or more interlocking segments of a split mould meet. Parting lines should be placed along geometric horizons to prevent silicone flash or ridges on the patient contact surface.

### Polyvinyl Alcohol (PVA / PVOH)
A synthetic water-soluble 3D printing filament. PVA dissolves completely in warm tap water (~45°C), allowing single-piece sacrificial moulds with complex anatomical undercuts to be cleanly removed without tearing delicate silicone features.

### Riser / Air Vent
A narrow auxiliary channel (~1.5 mm to 2.0 mm diameter) placed at the highest point of a mould cavity. As liquid silicone enters from the lowest point, trapped air escapes freely through the vents, preventing air pockets.

### Shore A Durometer
A standardized measurement scale (ASTM D2240) for elastomeric firmness:
- **Shore 00-10 to 00-30**: Gel-like silicone (soft cushions, extreme soft tissue).
- **Shore 10A to 20A**: Standard clinical bolus firmness (pliable, conforms to facial contours without compressing tissue).
- **Shore 30A to 50A**: Semi-rigid bolus (extremities, chest wall).

### Sprue
The main injection port (typically 4.0 mm to 6.0 mm internal diameter) designed into the lowest point of the mould cavity to accept liquid silicone from a dispensing syringe.

### Undercut
An anatomical recess, overhang, or protrusion (such as behind the ear, under the jaw, or across the bridge of the nose) that would prevent a cured part from being withdrawn cleanly from a single-piece rigid mould without tearing.

---

## 3. Computational Geometry & Mesh Topology

### 2-Manifold Mesh
A clean, continuous triangular surface mesh where every edge is shared by exactly two triangles, with no intersecting internal geometry or unstitched seams.

### Constructive Solid Geometry (CSG)
A 3D modeling technique that combines independent closed solid meshes using Boolean operations:
- **Union**: Merges separate 3D shapes into a single solid body.
- **Subtract (Difference)**: Carves one shape out of another (used to hollow out bolus cavities inside moulds).
- **Intersect**: Retains only the volume where shapes overlap.

### Euler Characteristic
A topological calculation used in 3D geometry software to verify that a triangle mesh is a closed, watertight surface without holes, open seams, or disconnected pieces.

### Volume-Preserving Morphological Smoothing
A two-step smoothing technique:
- **Inflate**: Expands the surface outward along surface normals, smoothing out sharp crevices and bridging CT slice stair-steps.
- **Deflate**: Contracts the boundary back inward by the exact same distance.

This removes stair-stepping without shrinking the bolus or thinning walls, ensuring the prescribed radiation dose is delivered accurately.

### Over-Erosion Guard
A built-in safety check in Fabolus that monitors bolus volume during smoothing. If smoothing alters total volume by more than a safe threshold (typically 1%), Fabolus warns the operator or clamps the smoothing offset distance to protect clinical accuracy.

### Parallel Transport Frame (Bishop Frame)
A stable mathematical method for sweeping a circular cross-section along a curved 3D line without twisting or pinching. Fabolus uses this to generate clean, un-twisted air channels and injection sprues.

### Signed Distance Field (SDF)
A 3D grid representation where every point records its shortest distance to the nearest surface (negative inside, positive outside, zero on the surface). This allows fast, error-free surface offsets and mould carving without self-intersections.

### Watertight (Closed) Mesh
A 3D mesh that forms a completely enclosed solid volume with zero holes or open boundaries, ensuring reliable 3D slicing and accurate physical volume calculations.

---

## 4. Fabolus Pipeline & Architectural Concepts

### BaseMesh
The unmodified triangle mesh imported into Fabolus from an external source (such as an STL exported from a clinical TPS). It is held on the workspace entry (`MeshRecord.BaseMesh`) and serves as the immutable root against which the command pipeline is replayed.

### Command History Pipeline
An ordered, non-destructive list of `IMeshCommand` records held on the workspace entry (`MeshRecord.Commands`). Each command carries a static `Priority` from `CommandPriority`: `Transform = 10` (rotate, translate, smoothing), `TextEmboss = 15`, `Mould = 20`, and `MouldTextEmboss = 25`. Recording a command clears any existing commands with a strictly greater priority (they depended on geometry the new command changed); commands sharing a priority do not clear each other. Replaying the list against the `BaseMesh` reconstructs the current mesh.

The history lives on the entry rather than on the geometry so that it survives operations which replace the geometry outright — a boolean returns a mesh that is neither of its operands, and an entry whose history vanished at that point would forget it was a mould.

### Predictable Error Handling (Result and Maybe Patterns)
A programming pattern where operations explicitly return `Result<T>` (either a success value or a clear clinical diagnostic) or `Maybe<T>` (an optional reference without null pointers), ensuring that 3D geometry computations never crash silently or produce corrupted model files.

