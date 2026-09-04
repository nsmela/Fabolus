# Testing Strategy & Benchmarks

## Testing Computational Geometry in Healthcare

In clinical radiation oncology software, automated software verification cannot rely solely on synthetic unit tests with mocked return values. A subtle algorithmic drift or numerical instability in a boolean subtraction can create a non-manifold hole in a casting mould, causing a clinical bolus to leak or fail prior to a scheduled patient treatment.

Fabolus enforces a **pragmatic, multi-tier testing strategy** combining pure domain unit tests with native geometric integration benchmarks executed against real, calibrated patient boluses.

<!-- IMAGE_PLACEHOLDER: [Figure 16.1: Test Pyramid for Fabolus. Diagram illustrating pure domain unit tests (fast, in-memory), geometry engine integration tests with clinical STLs, and MVVM presentation tests. Dimensions: 800x400px.] -->

---

## Test Organization & Project Structure

The test harness is divided into two dedicated test assemblies:

```
tests/
├── Fabolus.Core.Tests/                  <── Pure Domain & Native Engine Tests (xUnit)
│   ├── Fixtures/                        <── GeometryEngineFixture, TestFileSystem
│   └── ...                              <── Workspace, Metadata, Features, MeshLib tests
│
├── Fabolus.Wpf.Tests/                   <── Presentation Layer & ViewModel Tests
│   └── Features/
│       └── Main/                        <── MainViewModelTests
│
└── files/                               <── Shared STL test assets (resolved by GeometryEngineFixture)
```

---

## The Shared STL Test Assets (`tests/files/`)

Geometry tests load real bolus meshes from the shared `tests/files/` folder via `GeometryEngineFixture.LoadStl(...)`. The folder currently contains:

<!-- IMAGE_PLACEHOLDER: [Figure 16.2: STL test assets. 3D renders of the bolus models in tests/files highlighting varied anatomical topologies. Dimensions: 1000x450px.] -->

| File | Anatomical Site |
| :--- | :--- |
| `ear_bolus.stl`, `ear_bolus_smoothed.stl` | Auricular / outer ear (raw and smoothed) |
| `nose_bolus.stl` | Nasal bridge & ala |
| `chin_bolus.stl` | Chin / submental |
| `eye_bolus.stl` | Periorbital |
| `scalp_bolus.stl`, `scalp_mould.stl` | Cranial shell (bolus and generated mould) |
| `larynx_bolus.stl`, `larynx small.stl` | Anterior neck |
| `mould_test.stl` | Mould-generation fixture |

---

## The `GeometryEngineFixture` Shared Harness

The native MeshLib-backed engine is shared across tests via an xUnit collection fixture ([`GeometryEngineFixture`](https://github.com/nsmela/Fabolus/blob/v1/tests/Fabolus.Core.Tests/Fixtures/GeometryEngineFixture.cs)), applied through `[Collection("GeometryEngine collection")]`:

```csharp
public class GeometryEngineFixture
{
    public IGeometryEngine Engine { get; }

    public GeometryEngineFixture()
    {
        Engine = new GeometryEngine(new TestFileSystem());
    }

    public IMesh LoadStl(string name);       // loads a mesh from tests/files/
    public string GetAssetPath(string name); // resolves an asset path, searching upward for /files
    public IMesh UnitCube();                 // synthetic unit-cube primitive
}
```

### Examples of Invariants Under Test:
1. **Watertightness**: Boolean and air-channel tests assert `Topology().IsWatertight` on their results (e.g. `Union_ReturnsCombinedWatertightMesh`, `StraightAirChannel_Generate_FullMode_ReturnsWatertightTube`), and repair/import tests assert a repaired or imported mesh is watertight.
2. **Command replay & base-mesh lifetime**: `CommandReplayTests` verify that `GetMeshAtStage` returns the expected mesh instance for a given priority and that `Apply` consumes a copy of the base, so the workspace mesh survives repeated replays.
3. **Non-destructive transforms & smoothing**: transform tests assert translation moves bounds by the exact offset; smoothing tests assert smoothing applies in place, does not stack when applied twice, and preserves an earlier translation in the final geometry.

---

## Running the Automated Test Suites

Tests can be executed via the .NET CLI or any standard test runner (Visual Studio Test Explorer, Rider, VS Code C# Dev Kit):

```bash
# Execute Core Domain & Geometry Engine integration tests
dotnet test tests/Fabolus.Core.Tests

# Execute UI & Presentation tests
dotnet test tests/Fabolus.Wpf.Tests
```

The `Fabolus.Core.Tests` suite exercises the native MeshLib-backed engine directly, so its run time is dominated by real CSG boolean and level-set offset operations rather than by mocked returns. Use `dotnet test` (optionally with `--logger "console;verbosity=detailed"`) to see the current test count and results.
