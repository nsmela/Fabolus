# Command-Replay Pipeline & Immutability

## Why Command-Replay?

In traditional 3D mesh modeling, operations are applied **destructively**: rotating a mesh mutates its vertex positions; smoothing runs an irreversible algorithm over the vertex buffers; generating a mould bakes a boolean subtraction.

This destructive model creates significant issues in clinical software:
1. **Geometric Degradation**: Repeating an operation (e.g. adjusting a smoothing parameter from 1.0 to 1.2 mm) stacks algorithms on top of previously altered geometry, degrading mesh quality.
2. **Coupled Stale State**: If a user generates a sacrificial mould, then decides to adjust the bolus rotation by 5 degrees, a traditional CAD tool either leaves an invalid stale mould or corrupts the scene.
3. **Undo Brittleness**: Storing deep copies of million-polygon meshes for every undo state consumes gigabytes of memory.

Fabolus solves this by adopting a **Command-Replay Pipeline**:
- Meshes store their original, pristine input state as a `BaseMesh`.
- Every feature operation implements [`IMeshCommand`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/IMeshCommand.cs).
- Adjusting parameters simply replaces the command record in metadata and replays the pipeline deterministically against `BaseMesh`.

---

## The `IMeshCommand` Interface

```csharp
public interface IMeshCommand
{
    int Priority { get; }
    Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh);
}
```

Every command declares:
1. **`Priority`**: Defines where the command sits in the execution sequence (e.g., transformations before mould generation).
2. **`Apply`**: A pure functional transition receiving the engine and the input mesh, returning a new `IMesh` result.

---

## Pipeline Priority Levels ([`CommandPriority`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandPriority.cs))

```csharp
public static class CommandPriority {
    /// <summary>Rotate, Translate, Smoothing - siblings, none depends on the others.</summary>
    public const int Transform = 10;

    /// <summary>Depends on whatever geometry the Transform-stage commands produced.</summary>
    public const int Mould = 20;
}
```

### 1. Replacement, Not Stacking
When a command is applied, [`MeshMetadata.WithCommand`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs#L142) checks if a command of the same runtime type already exists:
- If present, the old command is **replaced** (e.g., updating `SmoothSettings(Intensity = 1.0)` with `SmoothSettings(Intensity = 1.2)` does not run two smoothing passes; it replaces the parameters).

### 2. Cascading Invalidation
Any existing command with a **strictly greater priority** is automatically purged:
- If a user has a `MouldDefinition` (Priority 20) baked, and then applies a new `RotateCommand` (Priority 10), the pipeline recognizes that the mould was computed against the old pre-rotated geometry.
- The stale mould command is discarded, and the mesh cleanly reflects the new rotation without geometric artifacts.

---

## Replay Execution ([`CommandReplay`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandReplay.cs))

Replaying is handled by `CommandReplay.Apply`:

```csharp
public static Result<IMesh> Apply(IGeometryEngine engine, IMesh baseMesh, IEnumerable<IMeshCommand> commands) {
    IMesh current = baseMesh;
    foreach (var command in commands) {
        var result = command.Apply(engine, current);
        if (result.IsFailure) return result.Error;
        current = result.Value;
    }
    return Result<IMesh>.Success(current);
}
```

- When the user clicks **Clear Mould** or **Reset Smoothing**, the feature calls [`WithoutCommand<TCommand>()`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs#L156) and immediately replays the remaining commands over `BaseMesh`.
- The result is instantaneous, exact restoration to the intended state without storing multiple giant mesh clones in RAM.
