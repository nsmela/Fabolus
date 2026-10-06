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

- **One session runs untimed first**, on `eye_bolus.stl`, so the timed sessions do not pay for
  the first call of everything being compiled. On that scan, cold against warm on one laptop:
  the first convex mould 52 to 57 ms against 10 to 15 ms, finding air pockets about 20 ms
  against about 1 ms. One session is not enough for the runtime to optimise everything, which
  takes dozens of calls; it removes the first-call cost, and that is most of it.
- **A cold run is a different measurement.** `FABOLUS_BENCH_COLD` skips the warm-up to time a
  cold start on purpose. Compare a cold run only with other cold runs: cold and warm can
  disagree in either direction. Started cold, finding air pockets on `chin_bolus` took about
  3.4 ms against GeometryEngine `main`, three times out of three, and about 9 ms against
  `feat/boolean-query`; warmed up, or with tiering off, both took about 8 ms. Neither engine had
  changed that code. So the branch was not slow: cold `main` was unusually fast, and why is not
  known. It is the runtime's doing, not either engine's.
- **"move a channel, rebuild"** is the middle of five rebuilds, each with one channel in a
  slightly different place and the bolus unchanged. It is the step a user repeats most.
- **`gc a/b/c, paused n ms` after a step is the collections that fell inside it and how long
  they stopped it for.** The three counts nest: every collection / those that reached
  generation 1 or further / the full ones. So `gc 2/1/1` is two collections, one of them full,
  and `gc 4/4/4` is four full collections. On a rebuild row the counts and the pause are for all
  five rebuilds together and say so (`gc 11/11/11 over 5`), beside a time that is for one. They
  are the whole process's, so a collection another thread provoked is included.
- **A collection in a step is a reason to look closer, not to discount it.** A step may have been
  stopped by a collection it did not cause, or may have caused it by allocating more. The engine
  also declares its native memory to the collector, which makes native allocations trigger full
  collections inside the steps that made them. The pause says how much of a difference between
  two engines is collection: subtract it from each before concluding the step itself changed.
- **For a small difference you still doubt**, run both engines with `DOTNET_TieredCompilation=0`,
  which compiles everything fully the first time. Treat the result as a diagnostic only: it also
  turns off profile-guided optimisation, which the shipped app has, so those timings are not
  what a user sees.
- **"managed heap" at the end is a snapshot** of whatever had not been collected at that moment,
  so it depends on where the last collection happened to fall. Compare "after collecting".
- Run each engine at least twice, alternating, on an idle machine. One run is one sample.
