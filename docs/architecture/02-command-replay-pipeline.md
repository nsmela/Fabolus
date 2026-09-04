# Command-Replay Pipeline & Immutability

## Destructive Modeling vs. Parametric Mesh Pipelines

In general-purpose polygon editing software (such as Blender, Meshmixer, or MeshLab), geometry edits are applied **destructively**:
- Translating or rotating a mesh mutates its raw vertex coordinate buffer $\mathbf{V} \leftarrow \mathbf{R}\mathbf{V} + \mathbf{t}$.
- Smoothing runs an irreversible mathematical operation directly altering vertex coordinates.
- Booleans discard original manifold faces and stitch new intersecting topologies.

In medical device design and clinical radiation therapy, destructive modeling introduces severe risks:
1. **Geometric Compounding & Degradation**: If an operator adjusts a smoothing intensity from $1.0\text{ mm}$ to $1.2\text{ mm}$, a destructive tool runs the second algorithm over the already-smoothed geometry. The operations stack, degrading the underlying anatomical geometry.
2. **Coupled Stale State**: If a clinical user designs a sacrificial mould and then rotates the bolus by $5^\circ$ for better printability, a destructive tool leaves the mould un-rotated, creating an invalid clinical state.
3. **Memory Bloat**: Storing deep copies of 500,000-triangle meshes for every undo/redo state rapidly exhausts RAM.

**Fabolus solves this by implementing a non-destructive, priority-governed Command-Replay Pipeline.**

<!-- IMAGE_PLACEHOLDER: [Figure 11.1: The Command Replay State Machine. State diagram illustrating BaseMesh, Priority 10 Transform stage (Smoothing, Rotation, Translation), Priority 20 Mould stage, and the cascading invalidation flow. Dimensions: 900x450px.] -->

---

## Mathematical Formulation

A mesh $\mathcal{M}$ in Fabolus is defined as a pure, pristine base geometry $\mathcal{M}_{\text{base}}$ paired with an ordered list of high-level semantic commands:

$$\mathcal{M} = \left( \mathcal{C}_k \circ \mathcal{C}_{k-1} \circ \dots \circ \mathcal{C}_1 \right)(\mathcal{M}_{\text{base}})$$

When an operator imports an STL from a TPS, Fabolus freezes that original geometry as the immutable [`BaseMesh`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs#L176). All downstream modifications—smoothing, orientation, mould generation, and channel placement—are captured as immutable data records.

<!-- IMAGE_PLACEHOLDER: [Figure 11.2: Memory Footprint: Destructive Cloning vs. Parametric Command Replay. Chart comparing RAM consumption across 20 iterative parameter adjustments: 1.2 GB for deep-cloning vs. 45 MB for Command Replay. Dimensions: 800x400px.] -->

---

## The `IMeshCommand` Contract

Every feature operation in `Fabolus.Core` implements [`IMeshCommand`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/IMeshCommand.cs):

```csharp
public interface IMeshCommand
{
    int Priority { get; }
    Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh);
}
```

- **`Priority`**: An integer defining the dependency layer of the command in the pipeline hierarchy.
- **`Apply`**: A pure functional transition receiving the engine and an input mesh, returning a brand-new, clean `IMesh`.

---

## Pipeline Priority Stages ([`CommandPriority`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandPriority.cs))

Commands are organized into discrete dependency tiers, deliberately spaced to accommodate future features without renumbering:

```csharp
public static class CommandPriority {
    /// <summary>Rotate, Translate, Smoothing - siblings, none depends on the others.</summary>
    public const int Transform = 10;

    /// <summary>Depends on whatever geometry the Transform-stage commands produced.</summary>
    public const int Mould = 20;
}
```

### 1. Replacement, Not Stacking
When a command is recorded via [`MeshMetadata.WithCommand`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/MeshMetadata.cs#L142), Fabolus inspects the active command history:
- If a command of the **same runtime type** already exists (e.g. replacing a prior `SmoothSettings`), the old command is replaced in-place.
- Multiple adjustments to smoothing or rotation never stack or compound; they maintain a single, canonical parameter record.

### 2. Cascading Invalidation
If a command is applied with priority $P_{\text{new}}$, any existing command in the pipeline with a strictly greater priority is automatically purged:

$$\forall \mathcal{C}_i \in \text{Commands} : \operatorname{Priority}(\mathcal{C}_i) > P_{\text{new}} \implies \text{Purge}(\mathcal{C}_i)$$

- **Clinical Example**: A user creates a sacrificial mould (Priority 20). If they subsequently enter the **rotate** tab and apply a new rotation (Priority 10), the pipeline recognizes that the mould was generated against stale, un-rotated coordinates.
- The stale mould command is discarded, ensuring the viewport and workspace remain mathematically consistent at all times.

---

## Execution Mechanics ([`CommandReplay.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Core/Geometry/Metadata/CommandReplay.cs))

Replaying commands against the base mesh is managed deterministically:

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

### Stage Probing via `GetMeshAtStage`
To power visualization features (such as comparing a smoothed bolus against its un-smoothed predecessor or rendering ghosts), the pipeline can evaluate geometry at any arbitrary stage:

```csharp
public static Result<IMesh> GetMeshAtStage(IGeometryEngine engine, IMesh currentMesh, int priorityLevel)
```

This filters the command list to `c.Priority <= priorityLevel` and replays only the allowed commands against a clone of `BaseMesh`, providing historical geometric snapshots without mutating the active workspace model.
