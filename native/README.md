# fabolus_geometry

A small native library giving `Geometry.Manifold` a fast, robust signed distance field, built on
[libigl](https://github.com/libigl/libigl).

**It is optional.** Everything it does has a managed implementation behind `MeshBvh`, and
`GeometryNative.IsAvailable` is false when the binary is absent, so a build without it is slower
rather than broken. The test suite passes either way.

## Why it exists

Manifold's level-set mesher asks for a signed distance one point at a time. Answering those from
managed code costs a P/Invoke transition per sample, and a bolus offset is a few hundred thousand
samples — the transitions, not the distances, are the expense. So `fabolus_offset` runs the
*whole* offset behind a single call and the per-sample work never leaves native code.

Measured on `sphere.stl`, offset by 2mm, identical output either way:

| | time |
|---|---|
| managed field, per-sample P/Invoke | 535 ms |
| this shim | 212 ms |
| MeshLib, for reference | 100 ms |

Across the smoothing suite the Manifold engine goes from 1m33s to 23s.

## How it signs

The magnitude of every distance comes from libigl's AABB tree, which gives the exact
closest-point distance. Only the *sign* comes from the fast winding number. Neither of libigl's
own `signed_distance` helpers is used, and both were tried first:

- `signed_distance_fast_winding_number` returns `sqrt(d²) · (1 − 2|w|)` — the distance *scaled*
  by the winding number rather than merely signed by it. Away from 0 and 1 the magnitude is
  pulled toward zero, so an isosurface asked for at 1mm lands short of it. Measured on a bolus:
  up to 0.1mm short, a tenth of the offset.
- `signed_distance_pseudonormal` returns a true distance, but its sign disagreed with the winding
  number on roughly one sample in three hundred of the same bolus — enough scattered flips to
  grow an erode-dilate by 2mm.

Splitting them gives an exact magnitude and a sign that holds up on open or self-intersecting
input, where a normal test has no consistent inside to consult. That matters here: imported
patient scans are routinely neither closed nor clean. The `sign_mode` argument is retained in the
ABI but no longer selects anything.

## Building

Both dependencies are header-only and fetched by CMake, so there is nothing to install first
beyond a compiler and CMake 3.16+.

```bash
cmake -B build -S native -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release -j
```

The output is `fabolus_geometry.dll` on Windows, `fabolus_geometry.so` elsewhere. Drop it beside
the built assemblies — the same directory as `Geometry.Manifold.dll` and `manifoldc.dll` — or
into `runtimes/<rid>/native/`, both of which the managed resolver probes.

Windows, with the Visual Studio toolchain:

```powershell
cmake -B build -S native -G "Visual Studio 17 2022" -A x64
cmake --build build --config Release
copy build\Release\fabolus_geometry.dll src\Fabolus.Wpf\bin\x64\Debug\net8.0-windows\
```

`manifoldc` is **not** linked — it is resolved by name at runtime (see `src/manifold_dynamic.hpp`),
so no import library is needed and the shim loads on a machine without Manifold, reporting that
as a status rather than failing to load.

## Versioning

`FABOLUS_GEOMETRY_ABI_VERSION` is bumped whenever anything in `include/fabolus_geometry.h`
changes shape. The managed binding checks it on load and treats a mismatch as absent, so a stale
binary beside a newer assembly falls back to the managed path instead of marshalling against the
wrong struct layout.

## Licences

- libigl — MPL-2.0 (also offered under GPL-3.0; MPL is the one relied on here).
- Eigen — MPL-2.0.

Neither is modified, and both are header-only, so nothing of theirs is redistributed except as
compiled code inside this library. If these binaries are shipped, MPL-2.0 clause 3.2 requires
that recipients be told where to obtain the source of the covered files; the pinned tags in
`CMakeLists.txt` (`FABOLUS_EIGEN_TAG`, `FABOLUS_LIBIGL_TAG`) are what identifies them.
