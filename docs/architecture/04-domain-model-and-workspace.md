# Domain Model & State Management

## The `Workspace` Aggregate Root

The central aggregate root in `Fabolus.Core` is [`Workspace`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Workspace.cs).

### Immutability & Structural Sharing
`Workspace` is an **immutable aggregate**. Any mutation—adding a mesh, deleting a mesh, or updating an active selection—returns a new `Workspace` instance containing an updated read-only mesh dictionary:

```csharp
public Result<Workspace> AddMesh(IMesh mesh, bool setActive = true)
public Result<Workspace> RemoveMesh(Guid meshId)
public Result<Workspace> UpdateMesh(IMesh updatedMesh)
public Result<Workspace> SetActiveMesh(Guid? meshId)
```

Because `Workspace` instances are immutable records, race conditions between background geometry worker threads and UI dispatchers are structurally impossible.

---

## Mesh Metadata & Type-Safe Keys

Meshes carry descriptive, diagnostic, and parametric state inside [`MeshMetadata`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs).

### Why Type-Safe Keys?
Instead of loose string-indexed property bags or rigid giant DTOs, `MeshMetadata` uses strongly typed [`MetadataKey<T>`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MetadataKey.cs):

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

### Type Safety & Clean Access
Features read and write properties with compile-time type safety:
```csharp
// Writing property
var updated = metadata.WithProperty(CoreKeys.Name, "Smoothed Bolus");

// Batch mutations with single dictionary reallocation
var batch = metadata.WithProperties(m => m
    .Set(CoreKeys.Name, "Smoothed Bolus")
    .Set(MeshIOKeys.Stats, stats));

// Safe functional reading
Maybe<Guid> parentId = metadata.GetProperty(CoreKeys.DerivedFrom);
```

---

## Functional Error Handling: `Result<T>` and `Maybe<T>`

Fabolus avoids throwing exceptions for expected domain failures (e.g. non-manifold mesh, invalid slice plane, missing file). Instead, it uses functional monads:

### 1. `Result<T>` ([`Result.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Result.cs))
Encapsulates either a successful value or a strongly typed [`Error`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Result.cs#L101):
```csharp
public readonly struct Result<T> {
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T Value { get; }
    public Error Error { get; }
}
```

### 2. `Maybe<T>` ([`Maybe.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Common/Maybe.cs))
Replaces null references with an explicit option type:
```csharp
public readonly struct Maybe<T> {
    public bool HasValue { get; }
    public bool HasNoValue => !HasValue;
    public T Value { get; }
}
```
