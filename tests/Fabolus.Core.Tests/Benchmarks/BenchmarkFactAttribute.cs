using Xunit;

namespace Fabolus.Tests.Benchmarks;

/// <summary>
/// A benchmark, not a test: it asserts nothing and only prints timings. Skipped unless
/// FABOLUS_BENCH is set, so a normal test run neither pays for it nor fails on a slow machine.
/// The same switch the view benchmarks in Fabolus.Wpf.Tests use.
/// </summary>
public sealed class BenchmarkFactAttribute : FactAttribute
{
    public BenchmarkFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FABOLUS_BENCH")))
        {
            Skip = "Benchmark. Run with FABOLUS_BENCH=1.";
        }
    }
}
