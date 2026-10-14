using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Wpf.Features.Main;

namespace Fabolus.Wpf.Tests;

/// <summary>
/// Records the viewport's loading overlay going up and down, which is how a caller outside a view
/// model can watch an operation start and finish.
/// </summary>
public sealed class LoadingLog
{
    public int Raised { get; private set; }
    public int Lowered { get; private set; }

    /// <summary>
    /// Starts recording what <paramref name="messenger"/> carries. The returned log is the
    /// recipient, and the caller holds it, so the strong-reference messenger keeps the
    /// registration alive for as long as the test needs it.
    /// </summary>
    public static LoadingLog Watching(IMessenger messenger)
    {
        var log = new LoadingLog();
        messenger.Register<LoadingLog, IsLoadingMessage>(log, (r, m) => r.Record(m.IsLoading));
        return log;
    }

    public void Record(bool isLoading)
    {
        if (isLoading) Raised++; else Lowered++;
    }

    public void Reset()
    {
        Raised = 0;
        Lowered = 0;
    }
}
