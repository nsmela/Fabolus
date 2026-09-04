# 3MF Interchange Specification

## Beyond Flat STL: The 3MF Container Format

While STL (Standard Tessellation Language) is universally supported by 3D printer slicers, it suffers from critical limitations:
- It stores only bare uncolored triangles.
- It contains zero metadata, units, or coordinate origins.
- It has no concept of assemblies, multi-part objects, or parametric history.

Fabolus adopts the **3D Manufacturing Format (3MF)** as its native project and export format. A `.3mf` file is an OPC (Open Packaging Convention) ZIP archive containing XML geometry models, color definitions, and custom extension metadata.

---

## The Fabolus 3MF Extension Schema

Fabolus defines a custom XML namespace for clinical bolus metadata:

```xml
xmlns:fab="http://fabolus.io/2026/metadata"
```

Inside the root `3D/3dmodel.model` document, Fabolus embeds two critical extensions:

### 1. Serialized Command Lineage (`fab:Commands`)
The entire non-destructive history of operations applied to the bolus is serialized as JSON within the standard 3MF `<metadata>` header:

```xml
<model xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02" 
       xmlns:fab="http://fabolus.io/2026/metadata" 
       unit="millimeter">
  <metadata name="fab:Commands">
    [
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
          "AirChannels": [...]
        }
      }
    ]
  </metadata>
  ...
</model>
```

### 2. Base Mesh Resource Embedding (`fab:role="basemesh"`)
To enable full non-destructive editing when reopening a saved project, the pristine pre-processed base mesh is stored directly in the `<resources>` section:

```xml
<resources>
  <object id="1" type="model">
    <!-- Active Printable Mould Geometry -->
    <mesh>...</mesh>
  </object>
  <object id="2" type="other" fab:role="basemesh">
    <!-- Pristine Pre-Processed CT Bolus Geometry -->
    <mesh>...</mesh>
  </object>
</resources>
<build>
  <item objectid="1" />
</build>
```

- Standard 3D slicers (Bambu Studio, PrusaSlicer) read `<item objectid="1" />` and slice the printable mould. Because Object 2 is marked `type="other"`, slicers ignore it, preventing duplicate printing.
- When **Fabolus** re-imports the 3MF file via [`GeometryIO.Import3MF`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/GeometryIO.cs#L125), it detects `fab:role="basemesh"`, restores `BaseMesh`, deserializes `fab:Commands`, and reconstructs the full interactive state.
