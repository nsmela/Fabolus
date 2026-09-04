# Domain Model & State Management

## Domain-Driven Design (DDD) in Fabolus

`Fabolus.Core` is built upon strict Domain-Driven Design (DDD) principles. The domain model enforces business invariants, guarantees topological consistency, and eliminates shared mutable state across asynchronous threads.

<!-- IMAGE_PLACEHOLDER: [Figure 13.1: Domain Entity-Relationship Diagram. UML class diagram illustrating Workspace aggregate root, IMesh interface, MeshMetadata value record, MetadataKey strongly typed descriptors, and Result monads. Dimensions: 900x500px.] -->

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

## The `Workspace` Aggregate Root ([`Workspace.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Workspace.cs))

The central aggregate root of the domain is `Workspace`.

### 1. Immutability & Structural Sharing
`Workspace` is an **immutable record**. Methods do not mutate internal dictionary state; instead, they return a new `Result<Workspace>` representing the updated state:

```csharp
public Result<Workspace> AddMesh(IMesh mesh, bool setActive = true)
public Result<Workspace> RemoveMesh(Guid meshId)
public Result<Workspace> UpdateMesh(IMesh updatedMesh)
public Result<Workspace> SetActiveMesh(Guid? meshId)
```

Because instances are immutable, a background worker thread calculating a boolean mould can safely read from its captured `Workspace` instance without locking, while the UI thread renders or navigates another view.

### 2. Ownership & Memory Contracts
In computational geometry, passing million-polygon meshes around carelessly causes rapid memory fragmentation. `Workspace` enforces a strict memory ownership contract:
- **Meshes Passed In** (`AddMesh`, `UpdateMesh`) are **consumed**. The caller surrenders ownership to the workspace.
- **BaseMesh Seeding**: The moment a mesh enters the workspace via `AddMesh`, if it lacks a `BaseMesh`, the workspace automatically establishes its initial state as the pristine base anchor for the command-replay pipeline.
- **Read-Only Inspection**: ViewModels and UI panels should **never fetch heavy geometry** just to read a name or check volume. Instead, they access [`Workspace.MeshMetadataList`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Workspace.cs#L23) or [`GetActiveMeshMetadata()`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Workspace.cs#L157). `MeshMetadata` is a pure managed value object with zero unmanaged memory overhead.

---

## The `MeshMetadata` Value Object & Type-Safe Keys

Rather than using loosely typed string-to-object dictionaries, Fabolus uses strongly-typed [`MetadataKey<T>`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MetadataKey.cs) descriptors:

```csharp
public sealed record MetadataKey<T>(string Name);

public static class CoreKeys {
    public static readonly MetadataKey<Guid> Id = new("Core.Id");
    public static readonly MetadataKey<string> Name = new("Core.Name");
    public static readonly MetadataKey<Guid> DerivedFrom = new("Core.DerivedFrom");
    public static readonly MetadataKey<string> CreatedBy = new("Core.CreatedBy");
    public static readonly MetadataKey<IMesh> BaseMesh = new("Core.BaseMesh");
    public static readonly MetadataKey<IReadOnlyList<IMeshCommand>> Commands = new("Core.Commands");
}
```

### High-Performance Batch Mutations
Modifying immutable dictionaries one property at a time produces multiple intermediate allocations. To maximize performance, `MeshMetadata` supports single-allocation batch updates:

```csharp
var updatedMetadata = activeMesh.Metadata.WithProperties(m => m
    .Set(CoreKeys.Name, "Smoothed Bolus")
    .Set(MeshIOKeys.Stats, computedStats)
    .Set(MeshIOKeys.Topology, topologyValidation));
```

This allocates a temporary builder, applies all mutations, and freezes it back into an immutable record with a single allocation.

---

## Functional Error Handling: `Result<T>` and `Maybe<T>`

Fabolus adopts **Railway-Oriented Programming (ROP)**. Domain errors (such as attempting to calculate volume on an open shell or dividing a mesh with an invalid normal) are normal, anticipated clinical occurrences, not runtime crashes.

<!-- IMAGE_PLACEHOLDER: [Figure 13.2: Railroad-Oriented Programming Flow. Flowchart illustrating Result<T> failure short-circuiting across feature workflows without throwing exceptions. Dimensions: 800x350px.] -->

### 1. The `Result<T>` Monad ([`Result.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Result.cs))
Methods that can fail return `Result<T>`, which encapsulates either a successful value or a strongly-typed [`Error`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Result.cs#L101):

```csharp
public Result<Workspace> Execute(Workspace workspace, SmoothSettings settings)
{
    var getMeshResult = workspace.GetActiveMesh();
    if (getMeshResult.IsFailure) return getMeshResult.Error;

    var activeMesh = getMeshResult.Value;
    ...
}
```

### 2. The `Maybe<T>` Option Monad ([`Maybe.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Maybe.cs))
Null references are completely eliminated from the domain layer. Any optional value (such as a parent mesh reference or smoothing settings) returns `Maybe<T>`:

```csharp
Maybe<SmoothSettings> smoothing = metadata.GetSmoothing();
if (smoothing.HasValue) {
    Console.WriteLine($"Smoothing Intensity: {smoothing.Value.Intensity} mm");
}
```
