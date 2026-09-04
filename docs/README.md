# Fabolus Wiki & Documentation

Welcome to the **Fabolus** project documentation. Fabolus is an open-source, specialized CAD/CAM application engineered for radiation oncology to assist medical physicists, radiation therapists, and clinical 3D printing engineers in designing patient-specific radiotherapy boluses and sacrificial silicone casting moulds.

<!-- IMAGE_PLACEHOLDER: [Figure 0.1: Fabolus Application Overview. Full-screen capture of the Fabolus main window displaying an anatomical bolus mesh loaded in the DirectX 3D viewport, with the step navigation header, interactive transform gizmo, and real-time physical properties panel.] -->

---

## Documentation Navigation

This documentation is organized into two primary tracks:
1. **[User & Clinical Guides](#user--clinical-guides)**: Step-by-step clinical workflows, algorithmic explanations, 3D printing parameters, and silicone casting protocols.
2. **[Architecture & Developer Manual](#architecture--developer-manual)**: System architecture, native C++ interop, the command-replay pipeline, domain models, and UI scene management.

---

## User & Clinical Guides

These guides walk through the full clinical and manufacturing lifecycle—from CT contour import in the Treatment Planning System (TPS) to curing bubble-free silicone boluses.

| Document | Description |
| :--- | :--- |
| **[01. Clinical Overview & Bolus Physics](user-guide/01-clinical-overview.md)** | The role of bolus in photon/electron radiation therapy, skin sparing, dose buildup, and why sacrificial silicone casting outperforms direct 3D printing. |
| **[02. Quickstart: 15-Minute Bolus Workflow](user-guide/02-quickstart-workflow.md)** | End-to-end tutorial: Import $\rightarrow$ Repair $\rightarrow$ Smooth $\rightarrow$ Orient $\rightarrow$ Mould $\rightarrow$ Vent $\rightarrow$ Export. |
| **[03. Mesh Inspection & Automated Repair](user-guide/03-mesh-inspection-and-repair.md)** | Reading volume, surface area, and bounding dimensions. Diagnosing manifold defects, open edges, and applying automated mesh repairs. |
| **[04. Volume-Preserving Smoothing](user-guide/04-volume-preserving-smoothing.md)** | Morphological erosion-dilation smoothing. Reading signed distance deviation heatmaps and using interactive cross-section clipping planes to verify wall thickness. |
| **[05. Print Orientation & Overhangs](user-guide/05-print-orientation-and-overhangs.md)** | 3-axis rotation gizmo, overhang angle analysis (45° Warning / 65° Critical), dynamic gradient vertex coloring, and minimizing support scarring. |
| **[06. Sacrificial Mould Design](user-guide/06-sacrificial-mould-design.md)** | Mould geometries (Convex Hull, Concave Shadow, Contoured Shell), XY/Z clearance offsets, cavity subtraction, and material optimization. |
| **[07. Air Channels & Degassing](user-guide/07-air-channels-and-degassing.md)** | Fluid dynamics of silicone injection. Configuring Straight sprues, Angled arc vents, and freehand Painted surface paths to eliminate air bubbles. |
| **[08. Mould Splitting & Demoulding](user-guide/08-mould-splitting-and-cuts.md)** | Planar cutting for multi-part moulds, keying alignments, and demoulding geometries with severe anatomical undercuts. |
| **[09. Slicing, Printing & Silicone Casting](user-guide/09-slicing-printing-and-casting.md)** | Recommended filaments (PVA, PLA, PETG), vase mode slicing, vacuum chamber degassing, silicone injection, curing, and chemical dissolution. |

---

## Architecture & Developer Manual

Detailed technical specifications for software engineers extending or maintaining the Fabolus platform.

| Document | Description |
| :--- | :--- |
| **[01. System Architecture](architecture/01-system-architecture.md)** | Clean/Hexagonal Architecture: `Fabolus.Core` (domain), `Geometry.MeshLib` (native adapter), `Fabolus.Wpf` (presentation). |
| **[02. Command-Replay Pipeline](architecture/02-command-replay-pipeline.md)** | The immutable `IMeshCommand` pipeline, `CommandPriority`, cascading invalidation, and non-destructive replay against `BaseMesh`. |
| **[03. Geometry Engine & Native MeshLib](architecture/03-geometry-engine-and-meshlib.md)** | Integration with MeshInspector C++ `MeshLib`, unmanaged memory lifecycle, polygon offsetting via `Clipper2Lib`, and 3D swept tubes. |
| **[04. Domain Model & State Management](architecture/04-domain-model-and-workspace.md)** | The `Workspace` aggregate root, `IMesh` contract, `MeshMetadata` type-safe key-value system, and functional error handling (`Result<T>`). |
| **[05. WPF MVVM & Scene Managers](architecture/05-wpf-mvvm-and-scene-managers.md)** | CommunityToolkit.Mvvm patterns, decoupling DirectX 11 rendering via `ISceneManager`, and messaging with `WeakReferenceMessenger`. |
| **[06. 3MF Interchange Specification](architecture/06-3mf-interchange-specification.md)** | Custom XML schema (`fab:Commands`), base mesh resource embedding (`fab:role="basemesh"`), and lossless project round-tripping. |
| **[07. Testing Strategy & Benchmarks](architecture/07-testing-strategy.md)** | Unit and integration testing practices, `GeometryEngineFixture`, synthetic primitives, and anatomical test suites. |

---

## Reference & Appendices

- **[Clinical Glossary](reference/clinical-glossary.md)**: Radiotherapy and geometric terminology (Bolus, PTV, CTV, HU, Manifold, Degassing, Riser, Sprue).
- **[Configuration & Preferences](reference/configuration-and-preferences.md)**: Strongly-typed preference keys, print bed dimensions, rendering themes, and experimental feature flags.

---

## High-Level Clinical Workflow

```mermaid
flowchart LR
    A["CT Scan / TPS Contour (STL)"] --> B["Fabolus: Inspect & Repair"]
    B --> C["Fabolus: Volume-Preserving Smoothing"]
    C --> D["Fabolus: Orient & Overhang Check"]
    D --> E["Fabolus: Sacrificial Mould & Vents"]
    E --> F["Export 3MF / STL"]
    F --> G["3D Print Mould (PVA / PLA)"]
    G --> H["Inject Medical Silicone"]
    H --> I["Cure & Dissolve / Demould"]
    I --> J["Patient-Fitted Silicone Bolus"]
```
