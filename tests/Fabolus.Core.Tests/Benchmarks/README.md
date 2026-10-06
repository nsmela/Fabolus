# Workflow benchmarks

`WorkflowBenchmarks` times what a user waits for through one session on a scan: open it, smooth
it, colour its overhangs, find its air pockets, build a mould of each kind, move a channel and
build it again, commit it, save it, and open the saved package. It drives the same feature
classes the app does, on the scans in `tests/files`.

It is a benchmark, not a test. It asserts nothing, prints timings, and is skipped unless
`FABOLUS_BENCH` is set. The view benchmarks in `Fabolus.Wpf.Tests` work the same way.

## Running it

```
set FABOLUS_BENCH=1
dotnet test tests/Fabolus.Core.Tests -c Release --filter "FullyQualifiedName~WorkflowBenchmarks" --logger "console;verbosity=detailed"
```

| Variable | Effect |
|---|---|
| `FABOLUS_BENCH` | Any value runs the benchmarks. Unset, they are skipped. |
| `FABOLUS_BENCH_LABEL` | A name for the engine under test, printed in the header. |
| `FABOLUS_BENCH_SCANS` | Comma-separated file names from `tests/files`, in place of the default four. |
| `FABOLUS_BENCH_OUT` | A file to append the whole report to. |
| `FABOLUS_BENCH_COLD` | Any value skips the untimed warm-up session, to time a cold start. |

## Comparing two versions of GeometryEngine

This is what it is for. Fabolus builds against a checkout of GeometryEngine, found through
`GeometryEngineRoot`, so the same benchmark can be built against two checkouts and the reports
set side by side:

```
set FABOLUS_BENCH=1
set FABOLUS_BENCH_OUT=%CD%\bench-main.txt
set FABOLUS_BENCH_LABEL=main
dotnet test tests/Fabolus.Core.Tests -c Release --filter "FullyQualifiedName~WorkflowBenchmarks" -p:GeometryEngineRoot=C:/path/to/engine-main/

set FABOLUS_BENCH_OUT=%CD%\bench-branch.txt
set FABOLUS_BENCH_LABEL=feat/boolean-query
dotnet test tests/Fabolus.Core.Tests -c Release --filter "FullyQualifiedName~WorkflowBenchmarks" -p:GeometryEngineRoot=C:/path/to/engine-branch/
```

`GeometryEngineRoot` must end in a slash. Each build replaces the last, so the header of every
report says what it was actually built against: when the engine assembly was built, whether it
keeps native solids, and whether it can describe a boolean before running it. Read that before
reading the numbers.

The benchmark uses only the parts of the engine's API that Fabolus already uses, so it compiles
against either checkout unchanged.

## Reading it

- **One session runs untimed first**, on `eye_bolus.stl`, so the runtime has compiled the code
  properly before anything is measured. Without it the first scan of a run pays for compilation
  (its first convex mould took about 270 ms with tiering off, against about 52 ms afterwards),
  and small steps differ between engines for no reason of the engines'. Set
  `FABOLUS_BENCH_COLD` to measure that cold start on purpose.
- **"move a channel, rebuild"** is the middle of five rebuilds, each with one channel in a
  slightly different place and the bolus unchanged. It is the step a user repeats most.
- **`gc a/b/c` after a step counts the collections that fell inside it, and the three numbers
  nest:** every collection / those that reached generation 1 or further / the full ones. So
  `gc 2/1/1` is two collections, one of them full, and `gc 4/4/4` is four full collections. On a
  rebuild row the counts are for all five rebuilds together. They are counted for the whole
  process, so a collection another thread provoked is included.
- **A collection in a step is a reason to look closer, not to discount it.** A step may have been
  interrupted by a collection it did not cause, or may have caused it by allocating more. The
  engine also declares its native memory to the collector, which makes native allocations
  trigger full collections inside the steps that made them. Run it again before deciding.
- **A small step can differ between engines because of compilation, not code.** In the first
  comparison, on a Windows laptop, finding air pockets on `chin_bolus` took 3.4 ms on `main` and
  9 ms on the branch, through code neither had changed. With `DOTNET_TieredCompilation=0` the
  gap closed, there and on a Linux machine (2.4 ms and 2.0 ms). The warm-up session is there to
  prevent this. If you still doubt a small difference, run both engines with that variable set,
  and treat the result as a diagnostic only: it also turns off profile-guided optimisation,
  which the shipped app has, so those timings are not what a user sees.
- **"managed heap" at the end is a snapshot** of whatever had not been collected at that moment,
  so it depends on where the last collection happened to fall. Compare "after collecting".
- Run each engine at least twice, alternating, on an idle machine. One run is one sample.
