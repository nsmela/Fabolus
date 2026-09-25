# Domain Model & State Management

## Predictable Domain Design

`Fabolus.Core` is built with a focus on clinical safety, predictable data flow, and **immutability** (data that cannot be changed once created). Rather than modifying existing 3D models in place, every operation produces a clean new state. This prevents data corruption, ensures safe multi-threading (so intensive 3D calculations do not freeze or disrupt the user interface), and maintains a reliable history of every patient model.

<!-- IMAGE_PLACEHOLDER: [Figure 13.1: Domain Architecture Diagram. Component diagram illustrating the Workspace container, its entries pairing IMesh with MeshRecord, and the FabolusAnnotations carried on the geometry. Dimensions: 900x500px.] -->

```
┌─────────────────────────────────────────────────────────────┐
│                 Workspace (Aggregate Root)                  │
│  - ActiveMeshId : Guid                                      │
│  - _entries : IReadOnlyDictionary<Guid, (IMesh, MeshRecord)>│
└──────────────┬───────────────────────────┬──────────────────┘
               │ the entry's identity      │ the geometry
               ▼                           ▼
┌──────────────────────────────┐ ┌────────────────────────────┐
│    MeshRecord (Value Object) │ │       IMesh (Entity)       │
│  - Id          : Guid        │ │  - Vertices  : Vec3[]      │
│  - Name        : string      │ │  - Triangles : int[]       │
│  - Commands    : IMeshCommand│ │  - Metadata  : MeshMetadata│
│  - BaseMesh    : IMesh?      │ └─────────────┬──────────────┘
│  - PendingMould: MouldDef?   │               │ carries
└──────────────────────────────┘               ▼
                                 ┌────────────────────────────┐
                                 │    FabolusAnnotations      │
                                 │  - Stats    : MeshStatistics│
                                 │  - Topology : TopologyValid.│
                                 └────────────────────────────┘
```

The split between the two halves is the central idea, and it is worth stating plainly: **a mesh's identity does not live on the mesh.**

A boolean returns geometry that is neither of its operands. An import returns geometry the engine named itself. In both cases anything travelling on the mesh would, a moment later, be describing something that no longer exists — which is exactly what used to happen, silently, every time a mould was generated. So the workspace owns who an entry *is*, and the geometry is only what currently fills it.

What genuinely does describe the geometry — the measurements the engine computed from it — travels with it instead, as annotations.

---

## The `Workspace` Container ([`Workspace.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Core/Geometry/Workspace.cs))

`Workspace` is the top-level container holding the entire active project session. It tracks all loaded meshes, which mesh is currently selected, and the sequence of modifications applied to each shape.

### 1. Safe Multi-Threading & Immutability
`Workspace` is **immutable**. Methods do not modify internal lists; instead, they return a new `Result<Workspace>` representing the updated state:

```csharp
public Result<Workspace> AddMesh(IMesh mesh, MeshRecord record, bool setActive = true)
public Result<Workspace> RemoveMesh(Guid meshId)
public Result<Workspace> UpdateMesh(Guid meshId, IMesh mesh, MeshRecord? record = null)
public Result<Workspace> UpdateRecord(MeshRecord record)
public Result<Workspace> SetActiveMesh(Guid? meshId)
```

Because workspace objects never change in place, background worker threads (such as those generating moulds or calculating booleans) can safely read the workspace without lock contention, while the user interface continues rendering smoothly.

Note that `UpdateMesh` takes the id explicitly rather than reading it off the mesh. That is deliberate: the new geometry may have come from an operation that knows nothing about this workspace, so there is nothing on it to read.

### 2. Mesh Ownership
- **Consuming Inputs**: When a mesh is added (`AddMesh`) or updated (`UpdateMesh`), the workspace takes ownership of that entry.
- **Base Mesh Seeding**: The first time a mesh enters the workspace, Fabolus preserves the untouched geometry on the record as the anchor future recalculations replay against.
- **Lightweight Inspection**: User interface panels and ViewModels that only need a mesh's name or history read `GetRecord`, which carries no geometry at all.

Meshes are immutable values, so nothing here copies defensively and a caller may hold whatever it is handed. This was not always true — under the previous geometry backend a mesh owned native memory, and handing out a shared instance let a caller dispose the workspace's own geometry out from under it. Comments describing meshes as "owned copies the caller must dispose" are leftovers from that era wherever they still survive.

---

## The `MeshRecord`: identity and history ([`MeshRecord.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Core/Geometry/MeshRecord.cs))

A record is the workspace entry: who the mesh is, what has been done to it, and the pristine geometry that history replays against.

```csharp
public sealed record MeshRecord {
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string CreatedBy { get; init; }

    // The ordered list of commands applied to BaseMesh to produce the current geometry.
    public IReadOnlyList<IMeshCommand> Commands { get; init; }

    // The pristine geometry, before any of Commands were applied.
    public IMesh? BaseMesh { get; init; }

    // Mould settings still being edited, distinct from a recorded MouldDefinition.
    public MouldDefinition? PendingMould { get; init; }
}
```

Features read and write it through small per-feature extensions, so each feature's vocabulary stays in the feature's own folder:

```csharp
record.Rotation();          // the net rotation, or null
record.Smoothing();         // the applied SmoothSettings, or null
record.MouldDefinition();   // set only on a generated mould
record.TextDecals();        // every decal across both decal commands
```

### Commands and the priority cascade

`Commands` is ordered, and recording one replaces any existing command of the same type rather than stacking it — rotations compose into a single net quaternion rather than a pile of them. Recording also clears anything of a strictly greater `Priority`, because it depended on geometry that just changed: rotating a mesh invalidates the mould built around it, so recording a new `RotateCommand` drops the `MouldDefinition`. Removing a command cascades the same way.

---

## `FabolusAnnotations`: what the geometry knows about itself

Stats and topology are measurements of the geometry in hand, so they travel with it, in a single typed slot the engine carries but never reads ([`IMeshAnnotations`](https://github.com/nsmela/GeometryEngine)).

```csharp
public sealed record FabolusAnnotations(
    MeshStatistics? Stats = null,
    TopologyValidation? Topology = null) : IMeshAnnotations
{
    public IMeshAnnotations? Carry(MeshOperation operation) => operation switch {
        MeshOperation.Transform when Topology is not null => new FabolusAnnotations(Topology: Topology),
        _ => null,
    };
}
```

`Carry` is the whole contract. Every engine operation that derives one mesh from another asks it what survives:

| Operation | What survives | Why |
|---|---|---|
| `Transform` | Topology only | Moving vertices leaves connectivity alone, so the audit still reads the same. The bounds do not, and a scale changes the volume too. |
| `Rebuild` | Nothing | Offset, decimate, smooth and repair all replace the surface. |
| `Combine` | Nothing | A boolean's result is neither operand, so nothing either of them measured describes it. |

Both values are caches. Losing one costs a recomputation, never correctness.

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
    var recordResult = workspace.GetActiveRecord();
    if (recordResult.IsFailure) return recordResult.Error;

    var record = recordResult.Value.WithCommand(settings);
    ...
}
```

### 2. The `Maybe<T>` Optional Value ([`Maybe.cs`](https://github.com/nsmela/BasicResults/blob/main/src/BasicResults/Maybe.cs))
`Maybe<T>` wraps a value that may legitimately be absent, at boundaries where a caller must be made to check before using it — the engine's optional operands, for instance.

Within the domain model itself, optional values are plain nullable references instead: `record.Smoothing()` returns a `SmoothSettings?`, and the compiler's nullable analysis enforces the check without a wrapper in the way.

```csharp
if (record.Smoothing() is { } smoothing) {
    Console.WriteLine($"Smoothing Intensity: {smoothing.Intensity} mm");
}
```
