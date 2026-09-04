# Fabolus Documentation

Welcome to the **Fabolus** project documentation. Fabolus is an open-source, specialized CAD/CAM application engineered for radiation oncology to assist medical physicists, radiation therapists, and clinical 3D printing engineers in designing patient-specific radiotherapy boluses and sacrificial silicone casting moulds.

<!-- IMAGE_PLACEHOLDER: [Figure 0.1: Fabolus Application Overview. Full-screen capture of the Fabolus main window displaying an anatomical bolus mesh loaded in the DirectX 3D viewport, with the six-stage step navigation header, interactive transform gizmo, and real-time physical properties panel.] -->

---

## Documentation Tracks

- **[User & Clinical Guides](user-guide/01-clinical-overview.md)**: Clinical context, physics of boluses, step-by-step 3D preparation workflows, mould design, and silicone casting protocols.
- **[Architecture & Developer Manual](architecture/01-system-architecture.md)**: Clean architecture, native C++ MeshLib integration, the command-replay pipeline, domain abstractions, and WPF DirectX viewport scene management.
- **[Reference](reference/clinical-glossary.md)**: Radiotherapy and geometric terminology, configuration keys, and preferences.

---

## Clinical Workflow at a Glance

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

---

## Quick Links

### Clinical & User Guides
1. **[01. Clinical Overview & Bolus Physics](user-guide/01-clinical-overview.md)**
2. **[02. Quickstart: 15-Minute Bolus Workflow](user-guide/02-quickstart-workflow.md)**
3. **[03. Mesh Inspection & Automated Repair](user-guide/03-mesh-inspection-and-repair.md)**
4. **[04. Volume-Preserving Smoothing](user-guide/04-volume-preserving-smoothing.md)**
5. **[05. Print Orientation & Overhangs](user-guide/05-print-orientation-and-overhangs.md)**
6. **[06. Sacrificial Mould Design](user-guide/06-sacrificial-mould-design.md)**
7. **[07. Air Channels & Degassing](user-guide/07-air-channels-and-degassing.md)**
8. **[08. Mould Splitting & Demoulding](user-guide/08-mould-splitting-and-cuts.md)**
9. **[09. Slicing, Printing & Silicone Casting](user-guide/09-slicing-printing-and-casting.md)**

### Architecture & Engineering
1. **[01. System Architecture](architecture/01-system-architecture.md)**
2. **[02. Command-Replay Pipeline](architecture/02-command-replay-pipeline.md)**
3. **[03. Geometry Engine & Native MeshLib](architecture/03-geometry-engine-and-meshlib.md)**
4. **[04. Domain Model & State Management](architecture/04-domain-model-and-workspace.md)**
5. **[05. WPF MVVM & Scene Managers](architecture/05-wpf-mvvm-and-scene-managers.md)**
6. **[06. 3MF Interchange Specification](architecture/06-3mf-interchange-specification.md)**
7. **[07. Testing Strategy & Benchmarks](architecture/07-testing-strategy.md)**

### Reference
- **[Clinical & Technical Glossary](reference/clinical-glossary.md)**
- **[Configuration & Preferences](reference/configuration-and-preferences.md)**
