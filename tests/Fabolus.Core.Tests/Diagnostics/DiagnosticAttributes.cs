using Xunit;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// A diagnostic, not a test: it measures the parting pipeline on real bodies and prints what it
/// finds, and most of it asserts nothing. Skipped unless FABOLUS_DIAGNOSTICS is set, so a normal
/// test run - and CI, which runs everything - neither waits on it nor reads its findings as results.
/// The benchmarks are switched the same way, by FABOLUS_BENCH.
/// </summary>
public sealed class DiagnosticFactAttribute : FactAttribute
{
    public DiagnosticFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FABOLUS_DIAGNOSTICS")))
        {
            Skip = "Diagnostic. Run with FABOLUS_DIAGNOSTICS=1.";
        }
    }
}

/// <summary><see cref="DiagnosticFactAttribute"/> for a measurement taken over several inputs.</summary>
public sealed class DiagnosticTheoryAttribute : TheoryAttribute
{
    public DiagnosticTheoryAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FABOLUS_DIAGNOSTICS")))
        {
            Skip = "Diagnostic. Run with FABOLUS_DIAGNOSTICS=1.";
        }
    }
}
