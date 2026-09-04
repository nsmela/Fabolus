# 3MF Interchange Specification

## Beyond Flat STL: The Open Packaging Convention (OPC)

For decades, additive manufacturing in clinical healthcare has been held back by the **STL (Standard Tessellation Language)** format. Originally created in 1987, STL exhibits fatal deficiencies for modern medical engineering:
- **No Units**: STLs are unitless numbers. A bolus designed in millimeters can easily be misread by a slicer as inches, altering dimensions by 2,540%.
- **No Coordinate Origins or Assembly Context**: Multi-body relationships and alignment matrices are completely lost.
- **Zero Parametric History**: Once an operation is baked into an STL, the parameters that created it are permanently erased.

To establish a modern, reproducible digital thread, Fabolus adopts the **3D Manufacturing Format (3MF)** as its native project and exchange container. 

A `.3mf` file is a compressed Open Packaging Convention (OPC) ZIP archive containing structured XML documents:

```
bolus_project.3mf (ZIP Archive)
├── [Content_Types].xml
├── _rels/
│   └── .rels
└── 3D/
    ├── 3dmodel.model                      <── Primary XML model & Fabolus metadata
    └── _rels/
        └── 3dmodel.model.rels
```

<!-- IMAGE_PLACEHOLDER: [Figure 15.1: Anatomy of a Fabolus 3MF Package. Internal folder and XML schema hierarchy diagram showing [Content_Types].xml, 3D/3dmodel.model, custom fab namespace attributes, and base mesh resource embedding. Dimensions: 900x500px.] -->

---

## The Fabolus 3MF XML Extension Schema

To embed non-destructive parametric recipes without breaking compatibility with third-party slicers (such as Bambu Studio, PrusaSlicer, or Cura), Fabolus defines an official custom metadata namespace:

```xml
xmlns:fab="http://fabolus.io/2026/metadata"
```

Inside `3D/3dmodel.model`, Fabolus injects two non-destructive extensions:

### 1. Serialized Command History (`fab:Commands`)
The entire lineage of operations applied to the bolus is serialized as structured JSON within a standard `<metadata>` element:

```xml
<?xml version="1.0" encoding="utf-8"?>
<model xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02"
       xmlns:fab="http://fabolus.io/2026/metadata"
       unit="millimeter" xml:lang="en-US">
  <metadata name="fab:Commands">[
    {
      "Type": "SmoothSettings",
      "Data": {
        "Iterations": 1,
        "Intensity": 1.5,
        "Inflation": 0.2,
        "RemeshRatio": 1.0,
        "Resolution": 1.0
      }
    },
    {
      "Type": "RotateCommand",
      "Data": {
        "Rotation": { "X": 0.0, "Y": 0.2588, "Z": 0.0, "W": 0.9659 }
      }
    },
    {
      "Type": "ConvexMouldDefinition",
      "Data": {
        "OffsetXY": 2.0,
        "OffsetBottom": 2.0,
        "OffsetTop": 2.0,
        "AirChannels": [
          {
            "Id": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
            "Type": "Straight",
            "TipDiameter": 2.0,
            "ChannelDiameter": 5.0,
            "TipLength": 4.0,
            "PenetrationDepth": 1.0
          }
        ]
      }
    }
  ]</metadata>
```

### 2. Base Mesh Embedding as a Secondary Resource
To allow the project to be re-opened and re-edited without degrading geometry, the raw, un-smoothed CT bolus is embedded in the `<resources>` table:

```xml
  <resources>
    <!-- Object 1: The Active Printable Sacrificial Mould -->
    <object id="1" type="model">
      <mesh>
        <vertices>...</vertices>
        <triangles>...</triangles>
      </mesh>
    </object>

    <!-- Object 2: The Pristine Pre-Processed Base Bolus -->
    <object id="2" type="other" fab:role="basemesh">
      <mesh>
        <vertices>...</vertices>
        <triangles>...</triangles>
      </mesh>
    </object>
  </resources>

  <!-- Only the printable mould is declared for 3D slicing -->
  <build>
    <item objectid="1" />
  </build>
</model>
```

---

## Universal Slicer Compatibility

Fabolus's packaging strategy adheres strictly to the core 3MF Consortium specifications:
- **How Slicers See the File**: 3D slicers only slice items explicitly referenced in the `<build>` block (`<item objectid="1" />`). Because Object 2 is marked `type="other"` and has no entry in `<build>`, slicers completely ignore it. You can drag a Fabolus `.3mf` directly into Bambu Studio or PrusaSlicer and slice the mould immediately.
- **How Fabolus Re-Imports the File**: When opened in Fabolus, [`GeometryIO.Import3MF`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryIO.cs#L125) reads `fab:Commands`, looks for `fab:role="basemesh"`, restores the pristine base geometry into `BaseMesh`, and populates the parametric command pipeline.

<!-- IMAGE_PLACEHOLDER: [Figure 15.2: Round-Trip Project Restoration Flow. Diagram illustrating saving to 3MF and re-importing in a fresh Fabolus session with 100% parametric history preserved. Dimensions: 850x400px.] -->

---

## File Format Comparison Matrix

| Capability | Standard ASCII STL | Standard Binary STL | Generic 3MF | Fabolus Extended 3MF |
| :--- | :--- | :--- | :--- | :--- |
| **Units Defined** | No (unitless) | No (unitless) | Yes (Explicit $mm$) | **Yes (Explicit $mm$)** |
| **File Compression** | None (huge file sizes) | Moderate (~80 bytes/tri) | High (ZIP compressed) | **High (~85% smaller than STL)** |
| **Multi-Body Support** | No | No | Yes | **Yes (Mould + Cavity + Channels)** |
| **Watertight Manifold Check** | No | No | Implicit | **Enforced** |
| **Command Lineage** | No | No | No | **Yes (Complete JSON Recipe)** |
| **Pristine Base Restoration**| No | No | No | **Yes (`fab:role="basemesh"`)** |
| **Direct Slicer Ready** | Yes | Yes | Yes | **Yes (Automatic build binding)** |
