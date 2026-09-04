# Testing Strategy & Benchmarks

## Testing Philosophy

In CAD and computational geometry software, unit testing cannot be limited to mocking interfaces with synthetic return values. Geometric algorithms must be validated against **real, topological 3D mesh data** to catch:
- Numerical precision drift in floating-point calculations
- Boolean CSG degeneracies (coplanar faces, coincident edges)
- Memory leaks across native C++ interop boundaries
- Invariants in volume conservation and manifold topology

Fabolus maintains a comprehensive, pragmatic test suite in `tests/Fabolus.Core.Tests`.

---

## The Geometry Engine Test Fixture

All geometry engine tests inherit from or utilize [`GeometryEngineFixture`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Fixtures/GeometryEngineFixture.cs).

### Capabilities:
1. **Synthetic Primitives**: Helpers to generate perfect unit cubes, cylinders, spheres, and planes with verified vertex-triangle topology.
2. **Clinical Test Files**: An extensive library of real patient bolus STLs stored in `tests/files/`:
   - `ear_bolus.stl` & `ear_bolus_smoothed.stl` (complex auricular folds)
   - `nose_bolus.stl` (nasal bridge curvature)
   - `chin_bolus.stl` (mental depression)
   - `scalp_bolus.stl` & `scalp_mould.stl` (large surface shell)
   - `larynx_bolus.stl` (neck contour)
   - `test bolus 107mL.stl` & `test bolus 7mm.stl` (calibrated volume benchmarks)

---

## Core Test Suites

| Test Suite | File | What is Validated |
| :--- | :--- | :--- |
| **Booleans** | [`BooleansTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/MeshLib/BooleansTests.cs) | Union, subtraction, and intersection correctness; coplanar edge stability; empty intersection error states. |
| **Command Replay** | [`CommandReplayTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Core/CommandReplayTests.cs) | Replaying multiple chained commands; non-destructive reset; priority-based cascading invalidation. |
| **Mesh Smoothing** | [`SmoothingTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Features/SmoothingTests.cs) | Volume conservation invariants (volume must remain within clinical tolerances); decimation ratio limits; collapse guard on over-erosion. |
| **Mould Generation** | [`MouldsTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Features/MouldsTests.cs) | Convex hull, concave shadow, and contoured shell generation; cavity subtraction; air channel coring. |
| **Air Channels** | [`AirChannelsTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Features/AirChannelsTests.cs) | Straight, Angled, and Painted swept tube generation; parallel transport frame stability; path point clamping. |
| **Mesh IO** | [`MeshIOTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Features/MeshIOTests.cs) | STL, OBJ, and 3MF import/export; lossless round-trip of `fab:Commands` and base mesh resources. |
| **Workspace** | [`WorkspaceTests.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/tests/Fabolus.Core.Tests/Core/WorkspaceTests.cs) | Immutability contracts; active mesh selection; duplicate ID prevention; metadata isolation. |

---

## Running the Tests

To execute the test suite from the CLI:

```bash
# Run Core Domain & Geometry Engine tests
dotnet test tests/Fabolus.Core.Tests

# Run Presentation & ViewModel tests
dotnet test tests/Fabolus.Wpf.Tests
```

All 101 tests in `Fabolus.Core.Tests` pass with zero failures and execute in under 20 seconds.
