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
├── Fabolus.Core.Tests/                  <── Pure Domain & Native Engine Benchmarks (xUnit)
│   ├── Core/                           <── Workspace, Result, Metadata, CommandReplay
│   ├── Features/                       <── Smoothing, Transforms, Moulds, AirChannels, IO
│   ├── MeshLib/                        <── Booleans, Modifiers, Generators, Evaluators
│   ├── Fixtures/                       <── GeometryEngineFixture, Primitive Factories
│   └── files/                          <── Clinical STL Benchmark Dataset
│
└── Fabolus.Wpf.Tests/                   <── Presentation Layer & ViewModel Tests
    └── Features/
        └── Main/                       <── MainViewModelTests, Messenger Verification
```

---

## The Clinical STL Benchmark Suite (`tests/files/`)

All geometry tests leverage an included suite of real patient boluses exported from clinical Treatment Planning Systems:

<!-- IMAGE_PLACEHOLDER: [Figure 16.2: Clinical Benchmark STL Suite. High-resolution 3D renders of the 6 benchmark models in tests/files (ear_bolus, nose_bolus, chin_bolus, scalp_bolus, larynx_bolus, test bolus 107mL) highlighting varied anatomical topologies. Dimensions: 1000x450px.] -->

| Benchmark File | Anatomical Site | Geometric Complexity & Challenge | Verified Invariant |
| :--- | :--- | :--- | :--- |
| **`ear_bolus.stl`** | Auricular / Outer Ear | Re-entrant folds, high local curvature along helix rim. | Volume preservation during erosion-dilation smoothing. |
| **`nose_bolus.stl`** | Nasal Bridge & Ala | Steep bilateral walls; acute angle intersections. | Angled air channel generation without surface clipping. |
| **`chin_bolus.stl`** | Mental Depression | Concave submental transition zone. | Shadow concave silhouette mould boundary offset. |
| **`scalp_bolus.stl`** | Cranial Shell | Large surface area, thin $5\text{ mm}$ uniform cross-section. | Contoured 3D shell offset performance and memory efficiency. |
| **`larynx_bolus.stl`** | Anterior Neck | Severe CT slice-stepping artifacts along tracheal axis. | Automated watertight repair and slice bridging. |
| **`test bolus 107mL.stl`** | Calibrated Physical Phantom | Precisely calibrated $107.0\text{ mL}$ volumetric gold standard. | Divergence theorem volume evaluation matches $107.0 \pm 0.5\text{ mL}$. |

---

## The `GeometryEngineFixture` Shared Harness

Native C++ MeshLib instances must be initialized and linked cleanly during test runs. Fabolus uses an xUnit class fixture ([`GeometryEngineFixture`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Fixtures/GeometryEngineFixture.cs)):

```csharp
public class GeometryEngineFixture : IDisposable
{
    public IGeometryEngine Engine { get; }

    public GeometryEngineFixture()
    {
        Engine = new GeometryEngine(new TestFileSystem());
    }

    public IMesh CreateUnitCube(float size = 10.0f) { ... }
    public IMesh CreateCylinder(float radius = 5.0f, float height = 20.0f) { ... }
    public IMesh LoadClinicalBolus(string fileName) { ... }
}
```

### Key Architectural Invariants Verified:
1. **Volume Conservation Invariant**:
   Smoothing tests verify that for any clinical bolus $\mathcal{M}$, the volume delta satisfies:
   $$\frac{|V_{\text{smoothed}} - V_{\text{initial}}|}{V_{\text{initial}}} \le 0.01 \quad (1.0\%)$$
2. **Boolean Watertightness Invariant**:
   Mould tests verify that after subtracting the bolus cavity and coring multiple air channels, the resulting mould has **0 open boundary edges** and **0 non-manifold edges**.
3. **Command-Replay Invariant**:
   Verifies that applying commands $\mathcal{C}_1, \mathcal{C}_2$, clearing $\mathcal{C}_2$, and re-applying $\mathcal{C}_2'$ produces identical vertex topology to applying $\mathcal{C}_1, \mathcal{C}_2'$ directly to a fresh base mesh.

---

## Running the Automated Test Suites

Tests can be executed via the .NET CLI or any standard test runner (Visual Studio Test Explorer, Rider, VS Code C# Dev Kit):

```bash
# Execute Core Domain & Geometry Engine integration tests
dotnet test tests/Fabolus.Core.Tests

# Execute UI & Presentation tests
dotnet test tests/Fabolus.Wpf.Tests
```

### Benchmark Metrics
- **Total Tests**: **101 tests** in `Fabolus.Core.Tests`.
- **Pass Rate**: **100% (0 failures, 0 skipped)**.
- **Execution Time**: ~**16 to 19 seconds** for the entire suite (including CSG booleans and 3D level-set offsets over 300,000-triangle meshes).
