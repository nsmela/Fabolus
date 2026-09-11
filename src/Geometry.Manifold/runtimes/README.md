# Native Manifold binaries

Prebuilt binaries of [Manifold](https://github.com/elalish/manifold), the library behind
`Geometry.Manifold`. The project copies them flat into every build output, beside the assembly,
which is the first place `ManifoldNative`'s resolver looks.

## Contents

| file | size | SHA-256 | role |
|---|---|---|---|
| `win-x64/native/manifoldc.dll` | 136,704 B | `4265da60c8e9d4368694452fa7f75f5790c3954ab8d757d914b07608ecd066e4` | the C FFI surface (`MANIFOLD_CBIND=ON`), what `DllImport` binds to |
| `win-x64/native/manifold.dll` | 1,151,488 B | `5a7e18a0c47a1a8a57fdd6f2d2f982a8be0d7865274fd36ddaa7f4189683d55e` | the library proper, loaded as a dependency of `manifoldc.dll` |

Record the checksums when replacing these, so a change of binary is visible in review rather
than being an opaque blob diff.

**win-x64 only.** No Linux, macOS, or Windows-on-ARM binaries ship. On those platforms
`ManifoldNative.IsAvailable` is false and every operation that needs the kernel returns
`Manifold.Unavailable`; use the MeshLib engine there, or build the native library yourself
(recipe below) and drop it into the output directory.

## Provenance — NOT ESTABLISHED

These are the same two files as `nsmela/meshcsg`
(`src/MeshCsg.Engine/runtimes/win-x64/native/`), byte for byte — the checksums above match its
copies. Its own handover note records that they were built without a version resource, so their
origin cannot be recovered from the files, and lists filling that in as the one task nobody but
the person who built them can do.

What *is* established here: the C API surface is **exactly that of Manifold 3.5.1**. The
exported symbol sets were compared against a 3.5.1 build from source and are identical — 293
`manifold_*` entry points, no difference in either direction. That is what the binding in
`Internal/Native` is written against, so the two agree on every signature it uses.

What is still missing, and matters for bolus work under IEC 62304, where third-party software
version and build configuration are precisely what must be documented and re-verified on change:

- upstream tag and commit SHA
- toolchain (compiler and version)
- CMake flags, **especially whether TBB was enabled**, which changes both behaviour and
  performance
- who built them, and when

Without the tag the binaries cannot be rebuilt or patched for a security fix. Resolving this
means rebuilding from a known tag and replacing both files together.

## Rebuilding

```bash
git clone --branch v3.5.1 --depth 1 https://github.com/elalish/manifold.git
cd manifold
cmake -B build -DCMAKE_BUILD_TYPE=Release -DMANIFOLD_CBIND=ON -DMANIFOLD_TEST=OFF \
      -DMANIFOLD_PAR=ON -DBUILD_SHARED_LIBS=ON
cmake --build build --config Release -j
```

`MANIFOLD_PAR=ON` needs TBB on the machine and makes the level-set offset parallel, which is
where `Geometry.Manifold` spends most of its native time. The outputs are `manifoldc` and
`manifold`; both go into `win-x64/native/` here, and their checksums into the table above.

## Licence and attribution

Manifold is licensed under the Apache License 2.0. Copyright The Manifold Authors.

The full licence text is in `LICENSE.manifold.txt` beside this file, and is copied into the
build output next to the binaries so it travels with any redistribution, as clause 4(a)
requires. Upstream Manifold ships a `LICENSE` and no `NOTICE`, so clause 4(d) — which applies
only "If the Work includes a 'NOTICE' text file" — has nothing to propagate. That is recorded
explicitly so a later reader does not mistake its absence for an omission.
