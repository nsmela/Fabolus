# System Architecture

## Architectural Philosophy: Clean / Hexagonal Design

Fabolus v1 is structured following the principles of **Clean Architecture** (Ports and Adapters / Hexagonal Architecture). The core domain logic is decoupled entirely from graphics libraries, UI frameworks, and specific geometric kernel implementations.

```
┌─────────────────────────────────────────────────────────────┐
│                        Fabolus.Wpf                          │
│     Presentation Layer: MVVM, Controls, Themes, SharpDX     │
└───────────────┬─────────────────────────────┬───────────────┘
                │ references                  │ references
                ▼                             ▼
┌─────────────────────────────┐ ┌─────────────────────────────┐
│        Fabolus.Core         │ │      Geometry.MeshLib       │
│  Domain Entities, Workflows │ │  C++ Native Wrapper Adapter │
│   Commands, Workspace Root  │ │   MeshLib, Clipper2 Interop │
└───────────────▲─────────────┘ └─────────────▲───────────────┘
                │                             │
                └────────────── implements ───┘
```

---

## Component Breakdown

### 1. `Fabolus.Core` (`net8.0`)
- **Role**: Pure business logic and domain contracts.
- **Dependencies**: None (pure C#, no UI, no DirectX, no P/Invoke).
- **Key Responsibilities**:
  - Domain abstractions: [`IMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IMesh.cs), [`MeshMetadata`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs), [`Workspace`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Workspace.cs).
  - Feature workflows: [`SmoothMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Smoothing/SmoothMesh.cs), [`TransformMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Transforms/TransformMesh.cs), [`GenerateMould`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/Moulds/GenerateMould.cs), [`AirChannel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/AirChannels/AirChannel.cs), [`ImportMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/MeshIO/ImportMesh.cs), [`ExportMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Features/MeshIO/ExportMesh.cs).
  - Parametric pipeline: [`IMeshCommand`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/IMeshCommand.cs), [`CommandPriority`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandPriority.cs), [`CommandReplay`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandReplay.cs).
  - Functional error handling: [`Result<T>`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Result.cs) and [`Maybe<T>`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Maybe.cs).

### 2. `Geometry.MeshLib` (`net8.0`)
- **Role**: Native geometry engine adapter.
- **Dependencies**: `MeshLib` (v3.1.2.192 native C++ NuGet package by MeshInspector), `Clipper2Lib` (polygon offsetting), `System.IO.Compression` (3MF packaging).
- **Key Responsibilities**:
  - Implements the [`IGeometryEngine`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryEngine.cs) interface bundle:
    - [`IBooleans`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IBooleans.cs): Union, Subtract, Intersect.
    - [`IGeometryModifiers`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryModifiers.cs): Offset, Double Offset, Resize/Decimate.
    - [`IGeometryGenerators`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryGenerators.cs): 3D Tubes, Spheres, Extrusions, Shadows, Convex Hulls.
    - [`IGeometryEvaluators`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryEvaluators.cs): Normals, Overhangs, Volume, Area, Topology Diagnostics.
    - [`IGeometryTransforms`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryTransforms.cs): Rotations (quaternion/matrix), Translations.
    - [`IGeometryIO`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/IGeometryIO.cs): STL, 3MF, OBJ loaders/savers.
  - Encapsulates unmanaged memory pointers (`MR.Mesh`) safely inside internal [`MRMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Geometry.MeshLib/MRMesh.cs) classes, preventing native leaks from penetrating the core domain.

### 3. `Fabolus.Wpf` (`net8.0-windows7.0`, `win-x64`)
- **Role**: Presentation and interaction layer.
- **Dependencies**: `CommunityToolkit.Mvvm`, `MahApps.Metro`, `HelixToolkit.Wpf.SharpDX`.
- **Key Responsibilities**:
  - User interface components and styling (`SteelCyan.xaml`, `Colours.xaml`, `Buttons.xaml`).
  - ViewModels conforming to MVVM with observable state and command bindings.
  - [`ISceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Viewport/ISceneManager.cs) implementations that translate domain state into DirectX 11 materials, gizmos, and manipulators without leaking UI controls into ViewModels.
  - Messaging with `WeakReferenceMessenger` for decoupled event-driven updates.
