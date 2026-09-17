# Domain Model & State Management

## Predictable Domain Design

`Fabolus.Core` is built with a focus on clinical safety, predictable data flow, and **immutability** (data that cannot be changed once created). Rather than modifying existing 3D models in place, every operation produces a clean new state. This prevents data corruption, ensures safe multi-threading (so intensive 3D calculations do not freeze or disrupt the user interface), and maintains a reliable history of every patient model.

<!-- IMAGE_PLACEHOLDER: [Figure 13.1: Domain Architecture Diagram. Component diagram illustrating Workspace container, IMesh interface, MeshMetadata value record, MetadataKey strongly typed descriptors, and Result error containers. Dimensions: 900x500px.] -->

```
┌─────────────────────────────────────────────────────────────┐
│                 Workspace (Aggregate Root)                  │
│  - ActiveMeshId : Guid                                      │
│  - _meshes : IReadOnlyDictionary<Guid, IMesh>               │
└──────────────────────────────┬──────────────────────────────┘
                               │ owns 1..*
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                       IMesh (Entity)                        │
│  - Vertices : Vector3[]                                     │
│  - Triangles : int[]                                        │
│  - Metadata : MeshMetadata ───┐                             │
└───────────────────────────────┼─────────────────────────────┘
                                │ has 1
                                ▼
┌─────────────────────────────────────────────────────────────┐
│                 MeshMetadata (Value Object)                 │
│  - Properties : ImmutableDictionary<string, object>         │
│  - BaseMesh : Maybe<IMesh>                                  │
│  - Commands : IReadOnlyList<IMeshCommand>                   │
└─────────────────────────────────────────────────────────────┘
```

---

## The `Workspace` Container ([`Workspace.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Core/Geometry/Workspace.cs))

`Workspace` is the top-level container holding the entire active project session. It tracks all loaded meshes, which mesh is currently selected, and the sequence of modifications applied to each shape.

### 1. Safe Multi-Threading & Immutability
`Workspace` is an **immutable record**. Methods do not modify internal lists; instead, they return a new `Result<Workspace>` representing the updated state:

```csharp
public Result<Workspace> AddMesh(IMesh mesh, bool setActive = true)
public Result<Workspace> RemoveMesh(Guid meshId)
public Result<Workspace> UpdateMesh(IMesh updatedMesh)
public Result<Workspace> SetActiveMesh(Guid? meshId)
```

Because workspace objects never change in place, background worker threads (such as those generating moulds or calculating booleans) can safely read the workspace without lock contention, while the user interface continues rendering smoothly.

### 2. Mesh Ownership & Memory Safety
3D medical meshes contain millions of vertices and triangles. Passing them around carelessly can cause memory exhaustion. `Workspace` manages mesh memory carefully:
- **Consuming Inputs**: When a mesh is added (`AddMesh`) or updated (`UpdateMesh`), the workspace takes full ownership of that data.
- **Base Mesh Seeding**: The first time a mesh enters the workspace, Fabolus automatically preserves an untouched copy of its geometry as the anchor for future recalculations.
- **Lightweight Inspection**: User interface panels and ViewModels do not need to fetch heavy 3D geometry just to check a mesh's name, dimensions, or volume. Instead, they read `MeshMetadata`—a lightweight list of properties with virtually zero memory overhead.

---

## The `MeshMetadata` System & Type-Safe Keys

Rather than storing settings and properties in loose text dictionaries, Fabolus uses strongly-typed [`MetadataKey<T>`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Core/Geometry/Metadata/MetadataKey.cs) descriptors:

```csharp
public static class CoreKeys {
    public static readonly MetadataKey<Guid> Id = new("Id");
    public static readonly MetadataKey<string> Name = new("Name");
    public static readonly MetadataKey<Guid> DerivedFrom = new("Derived From");
    public static readonly MetadataKey<string> CreatedBy = new("Created By");
    public static readonly MetadataKey<IReadOnlyList<IMeshCommand>> Commands = new("Commands");

    // Stores the untouched base geometry the command list replays against.
    internal static readonly MetadataKey<IMesh> BaseMesh = new("Base Mesh");
}
```

### High-Performance Batch Updates
Updating immutable data property-by-property can cause extra memory allocations. To keep performance high, `MeshMetadata` allows multiple properties to be updated simultaneously in a single step:

```csharp
var updatedMetadata = activeMesh.Metadata.WithProperties(m => m
    .Set(CoreKeys.Name, "Smoothed Bolus")
    .Set(MeshIOKeys.Stats, computedStats)
    .Set(MeshIOKeys.Topology, topologyValidation));
```

This applies all changes at once and returns the updated metadata record in a single allocation.

---

## Predictable Error Handling: `Result<T>` and `Maybe<T>`

In clinical radiation therapy software, geometric edge cases—such as an imported mesh having holes, or a cut plane that misses the model entirely—are everyday occurrences, not system crashes.

Instead of throwing unhandled exceptions or returning `null` (which can cause sudden crashes), Fabolus wraps operations in two predictable types:

<!-- IMAGE_PLACEHOLDER: [Figure 13.2: Predictable Result<T> Error Handling Flow. Flowchart illustrating Result<T> success and failure pathways across feature workflows without unexpected runtime crashes. Dimensions: 800x350px.] -->

Both types come from [BasicResults](https://github.com/nsmela/BasicResults), a small standalone library shared with GeometryEngine so that a `Result` crossing the boundary between them is one type rather than two that have to be translated.

### 1. The `Result<T>` Container ([`Result.cs`](https://github.com/nsmela/BasicResults/blob/main/src/BasicResults/Result.cs))
Methods that can fail return `Result<T>`, which clearly indicates either `Success` (with the resulting mesh or value) or `Failure` (with a specific, user-friendly error message):

```csharp
public Result<Workspace> Execute(Workspace workspace, SmoothSettings settings)
{
    var getMeshResult = workspace.GetActiveMesh();
    if (getMeshResult.IsFailure) return getMeshResult.Error;

    var activeMesh = getMeshResult.Value;
    ...
}
```

### 2. The `Maybe<T>` Optional Value ([`Maybe.cs`](https://github.com/nsmela/BasicResults/blob/main/src/BasicResults/Maybe.cs))
`null` references are completely eliminated from the core layer. Any value that might not be present (such as whether a bolus has custom smoothing settings attached) is wrapped in `Maybe<T>`, ensuring code checks for the value before using it:

```csharp
Maybe<SmoothSettings> smoothing = metadata.GetSmoothing();
if (smoothing.HasValue) {
    Console.WriteLine($"Smoothing Intensity: {smoothing.Value.Intensity} mm");
}
``````
