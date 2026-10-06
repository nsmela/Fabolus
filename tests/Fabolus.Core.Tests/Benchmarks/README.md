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

- The first scan also pays for everything being compiled. Read trends from the later ones.
- "move a channel, rebuild" is the middle of five rebuilds, each with one channel in a slightly
  different place and the bolus unchanged. It is the step a user repeats most.
- Run each engine at least twice, alternating, on an idle machine. One run is one sample.
- `gc 2/1/1` after a step means two collections of the youngest generation, one of the middle
  and one full collection fell inside it. A collection is charged to whatever step it lands in,
  so a step that is slower on one engine and shows a collection the other does not was probably
  interrupted, not slowed.
- A step can also differ between two engines only because the runtime had not finished
  recompiling it yet. The first time this benchmark compared engines, finding air pockets on one
  scan took 3.4 ms on one and 9 ms on the other, through code neither engine had changed. With
  `DOTNET_TieredCompilation=0` set, which compiles everything fully the first time, the two came
  out at 2.4 and 2.0 ms. Before believing a small step got slower, run both engines that way.
- "managed heap" at the end is whatever had not been collected yet, so it rises when an engine
  allocates less and is collected less often. "after collecting" is what was really still in use.
- The memory lines are for the whole process at the end: what the sessions left it holding,
  and what remains after a full collection.
