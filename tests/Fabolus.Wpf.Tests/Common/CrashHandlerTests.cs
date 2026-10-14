using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Wpf.Common;
using Moq;

namespace Fabolus.Wpf.Tests.Common;

/// <summary>
/// The failures here go unhandled for real, on a running dispatcher - the same event the app's
/// Application.DispatcherUnhandledException is raised from - so these cover the path a crash
/// actually takes rather than a hand-built event.
/// </summary>
public sealed class CrashHandlerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fabolus-crash-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<IAlertDialog> _alert = new();
    private readonly List<string> _shown = [];
    private readonly CrashRedactor _redactor = new();

    public CrashHandlerTests()
    {
        _alert.Setup(a => a.ShowError(It.IsAny<string>())).Callback<string>(message =>
        {
            _shown.Add(message);
            _events.Add("dialog");
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    // Freezing, dialogs and termination, in the order they happened.
    private readonly List<string> _events = [];

    // Fixed unless a test moves it, so every failure in a test lands at the same instant.
    private DateTime _now = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    private CrashHandler NewHandler(IFileSystem? fileSystem = null) =>
        new(new CrashLog(fileSystem ?? new FileSystem(), _folder, () => DateTime.Now, "1.0.0", _redactor),
            _alert.Object,
            freezeUi: () => _events.Add("freeze"),
            terminate: () => _events.Add("terminate"),
            clock: () => _now);

    private string[] Logs() =>
        Directory.Exists(_folder) ? Directory.GetFiles(_folder, "crash-*.txt") : [];

    private string[] LogsContaining(string text) =>
        Logs().Where(p => File.ReadAllText(p).Contains(text)).ToArray();

    [Fact]
    public void UiThreadThrow_IsLoggedShownAndHandled()
    {
        OnDispatcher(async dispatcher =>
        {
            Post(dispatcher, () => throw new InvalidOperationException("ui thread failure"));
            await Drain(dispatcher);

            var log = Assert.Single(Logs());
            var report = File.ReadAllText(log);
            Assert.Contains(CrashHandler.UiThreadContext, report);
            Assert.Contains("System.InvalidOperationException: ui thread failure", report);

            var message = Assert.Single(_shown);
            Assert.Contains("Fabolus is still running", message);
            Assert.Contains("InvalidOperationException: ui thread failure", message);
            Assert.Contains(log, message);
        });
    }

    // A screenshot of the dialog is as likely to end up on an issue as the log.
    [Fact]
    public void Dialog_KeepsPathsAndMeshNamesOut()
    {
        _redactor.AddSensitiveNames(["Jane Doe"]);

        OnDispatcher(async dispatcher =>
        {
            Post(dispatcher, () => throw new IOException(@"Cannot read 'C:\Scans\Smith.stl' for Jane Doe"));
            await Drain(dispatcher);

            var message = Assert.Single(_shown);
            Assert.Contains("IOException: Cannot read '<path>' for <name>", message);
            Assert.DoesNotContain("Smith", message);
            Assert.DoesNotContain("Jane", message);
        });
    }

    [Fact]
    public void UiThreadThrow_LeavesTheDispatcherRunning()
    {
        OnDispatcher(async dispatcher =>
        {
            var ranAfter = false;
            Post(dispatcher, () => throw new InvalidOperationException("first"));
            Post(dispatcher, () => ranAfter = true);
            await Drain(dispatcher);

            Assert.True(ranAfter);
        });
    }

    // The parting split's live preview and cutter rebuild are async void, and catch only
    // cancellation - a geometry exception after the await is the crash this issue is about.
    [Fact]
    public void AsyncVoidThrowAfterBackgroundWork_IsCaught()
    {
        OnDispatcher(async dispatcher =>
        {
            async void RebuildPreview() =>
                await Task.Run(() => throw new InvalidOperationException("async void failure"));

            Post(dispatcher, RebuildPreview);
            await Until(dispatcher, () => _shown.Count > 0);

            Assert.Single(LogsContaining("async void failure"));
            Assert.Contains("async void failure", Assert.Single(_shown));
        });
    }

    // What [RelayCommand] generates for an async Task method: the toolkit rethrows a failed
    // execution on the dispatcher, which is how every import, repair and mould command fails.
    [Fact]
    public void AsyncRelayCommandFailure_IsCaught()
    {
        OnDispatcher(async dispatcher =>
        {
            var command = new AsyncRelayCommand(() =>
                Task.Run(() => throw new InvalidOperationException("command failure")));

            command.Execute(null);
            await Until(dispatcher, () => _shown.Count > 0);

            Assert.Single(LogsContaining("command failure"));
            Assert.Contains("command failure", Assert.Single(_shown));
            Assert.False(command.IsRunning);
        });
    }

    // A throw from something that runs every frame would otherwise put a dialog up as fast as
    // the user can close it.
    [Fact]
    public void RepeatsOfTheSameFailure_AreAllLoggedButShownOnce()
    {
        OnDispatcher(async dispatcher =>
        {
            static void Fail() => throw new InvalidOperationException("every frame");
            for (var i = 0; i < 5; i++) Post(dispatcher, Fail);
            await Drain(dispatcher);

            Assert.Equal(5, LogsContaining("every frame").Length);
            Assert.Single(_shown);
        });
    }

    // Doing the same thing again later is a new failure as far as the user is concerned.
    [Fact]
    public void SameFailureAfterAQuietGap_IsShownAgain()
    {
        OnDispatcher(async dispatcher =>
        {
            static void Fail() => throw new InvalidOperationException("same again");

            Post(dispatcher, Fail);
            await Drain(dispatcher);
            _now += CrashHandler.RepeatQuietPeriod + TimeSpan.FromMilliseconds(1);
            Post(dispatcher, Fail);
            await Drain(dispatcher);

            Assert.Equal(2, _shown.Count);
        });
    }

    // Each repeat restarts the quiet period, so something failing every frame stays quiet for as
    // long as it keeps failing - not only for the first second.
    [Fact]
    public void FailureThatKeepsRepeating_StaysQuietPastTheQuietPeriod()
    {
        OnDispatcher(async dispatcher =>
        {
            static void Fail() => throw new InvalidOperationException("every half second");

            for (var i = 0; i < 6; i++)
            {
                Post(dispatcher, Fail);
                await Drain(dispatcher);
                _now += CrashHandler.RepeatQuietPeriod / 2;
            }

            Assert.Equal(6, LogsContaining("every half second").Length);
            Assert.Single(_shown);
        });
    }

    [Fact]
    public void DifferentFailures_AreEachShown()
    {
        OnDispatcher(async dispatcher =>
        {
            Post(dispatcher, () => throw new InvalidOperationException("one"));
            await Drain(dispatcher);
            Post(dispatcher, () => throw new FormatException("two"));
            await Drain(dispatcher);

            Assert.Equal(2, _shown.Count);
        });
    }

    // A modal dialog pumps the dispatcher, so another failure can land while it is on screen.
    // That one is logged, but it does not stack a second dialog on the first.
    [Fact]
    public void FailureWhileTheDialogIsOpen_IsLoggedWithoutASecondDialog()
    {
        OnDispatcher(async dispatcher =>
        {
            var dialogs = 0;
            _alert.Setup(a => a.ShowError(It.IsAny<string>())).Callback(() =>
            {
                dialogs++;
                if (dialogs > 1) return;

                // Stand-in for MessageBox's modal loop: pump until everything queued so far is done.
                var modal = new DispatcherFrame();
                Post(dispatcher, () => throw new FormatException("while the dialog was open"));
                Post(dispatcher, () => modal.Continue = false, DispatcherPriority.ContextIdle);
                Dispatcher.PushFrame(modal);
            });

            Post(dispatcher, () => throw new InvalidOperationException("first"));
            await Drain(dispatcher);

            Assert.Equal(1, dialogs);
            Assert.Single(LogsContaining("while the dialog was open"));
        });
    }

    [Fact]
    public void LogThatCannotBeWritten_StillShowsTheErrorAndKeepsRunning()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.CreateDirectory(It.IsAny<string>())).Throws(new IOException("disk full"));

        OnDispatcher(async dispatcher =>
        {
            var ranAfter = false;
            Post(dispatcher, () => throw new InvalidOperationException("boom"));
            Post(dispatcher, () => ranAfter = true);
            await Drain(dispatcher);

            Assert.True(ranAfter);
            Assert.Contains("could not be saved to a log file", Assert.Single(_shown));
        }, fileSystem.Object);
    }

    [Fact]
    public void DialogThatThrows_DoesNotTakeTheAppDown()
    {
        _alert.Setup(a => a.ShowError(It.IsAny<string>())).Throws(new InvalidOperationException("no dialog"));

        OnDispatcher(async dispatcher =>
        {
            var ranAfter = false;
            Post(dispatcher, () => throw new InvalidOperationException("boom"));
            await Drain(dispatcher);
            Post(dispatcher, () => ranAfter = true);
            await Drain(dispatcher);

            Assert.True(ranAfter);
            Assert.Single(Logs());
        });
    }

    // Raising the real AppDomain event would end the test run with the process, so the handler
    // is called as the runtime would call it.
    [Fact]
    public void FatalBackgroundException_IsLoggedThenShown()
    {
        var handler = NewHandler();

        handler.OnFatalException(null, new UnhandledExceptionEventArgs(new InvalidOperationException("background failure"), isTerminating: true));

        var report = File.ReadAllText(Assert.Single(Logs()));
        Assert.Contains(CrashHandler.BackgroundThreadContext, report);
        Assert.Contains("background failure", report);
        Assert.Contains("has to close", Assert.Single(_shown));
    }

    // Left to the runtime, the window stayed up and usable for seconds after the notice while
    // Windows Error Reporting ran, then vanished mid-action - found by hand on this branch.
    [Fact]
    public void FatalException_FreezesTheWindowShowsTheNoticeThenEndsTheProcess()
    {
        NewHandler().OnFatalException(null, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: true));

        Assert.Equal(["freeze", "dialog", "terminate"], _events);
    }

    [Fact]
    public void FatalException_EndsTheProcessEvenIfTheNoticeCannotBeShown()
    {
        _alert.Setup(a => a.ShowError(It.IsAny<string>())).Throws(new InvalidOperationException("no dialog"));

        NewHandler().OnFatalException(null, new UnhandledExceptionEventArgs(new InvalidOperationException("boom"), isTerminating: true));

        Assert.Equal(["freeze", "terminate"], _events);
    }

    [Fact]
    public void RecoveredFailure_NeitherFreezesNorEnds()
    {
        OnDispatcher(async dispatcher =>
        {
            Post(dispatcher, () => throw new InvalidOperationException("recoverable"));
            await Drain(dispatcher);
        });

        Assert.Equal(["dialog"], _events);
    }

    // It is the last thing the user sees, so it shows even if that failure was shown before.
    [Fact]
    public void FatalException_IsShownEvenIfTheSameFailureWasShownBefore()
    {
        var handler = NewHandler();
        var failure = new InvalidOperationException("same failure");

        handler.OnFatalException(null, new UnhandledExceptionEventArgs(failure, isTerminating: true));
        handler.OnFatalException(null, new UnhandledExceptionEventArgs(failure, isTerminating: true));

        Assert.Equal(2, _shown.Count);
    }

    [Fact]
    public void FatalNonException_IsStillLogged()
    {
        NewHandler().OnFatalException(null, new UnhandledExceptionEventArgs("a thrown string", isTerminating: true));

        Assert.Single(LogsContaining("a thrown string"));
    }

    [Fact]
    public void UnobservedTaskException_IsLoggedAndObservedWithoutADialog()
    {
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("nobody awaited this")));

        NewHandler().OnUnobservedTaskException(null, args);

        Assert.True(args.Observed);
        var report = File.ReadAllText(Assert.Single(Logs()));
        Assert.Contains(CrashHandler.UnobservedTaskContext, report);
        Assert.Contains("nobody awaited this", report);
        Assert.Empty(_shown);
    }

    // End to end: a fire-and-forget task faults, is collected, and the runtime raises the event.
    // The event is process-wide, so other tests' stray tasks may land here too - look for ours.
    [Fact]
    public void UnobservedTaskException_IsLoggedWhenTheRuntimeRaisesIt()
    {
        var handler = NewHandler();
        handler.AttachProcessWide();
        try
        {
            var marker = "fire and forget " + Guid.NewGuid().ToString("N");
            FaultAndForget(marker);

            for (var attempt = 0; attempt < 10 && LogsContaining(marker).Length == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.Single(LogsContaining(marker));
        }
        finally
        {
            handler.DetachProcessWide();
        }
    }

    [Fact]
    public void StartupFailure_IsLoggedAndShownAsFatal()
    {
        NewHandler().ReportStartupFailure(new InvalidOperationException("no host"));

        Assert.Contains(CrashHandler.StartupContext, File.ReadAllText(Assert.Single(Logs())));
        Assert.Contains("has to close", Assert.Single(_shown));
        Assert.DoesNotContain("terminate", _events);   // App shuts down in order itself
    }

    // Kept out of the test's own frame so nothing there holds the task alive through the collect.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FaultAndForget(string marker)
    {
        var task = Task.Run(() => throw new InvalidOperationException(marker));

        // Wait for it to fault without touching task.Exception, which would observe it.
        task.ContinueWith(_ => { }, TaskContinuationOptions.ExecuteSynchronously).Wait();
    }

    /// <summary>
    /// Runs the body on a pumping dispatcher with a crash handler attached. A backstop goes on
    /// after it, so a failure the crash handler misses fails the test instead of ending the run.
    /// </summary>
    private void OnDispatcher(Func<Dispatcher, Task> body, IFileSystem? fileSystem = null)
    {
        var missed = new List<Exception>();

        UiThread.Run(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var handler = NewHandler(fileSystem);
            handler.Attach(dispatcher);
            dispatcher.UnhandledException += Backstop;
            try
            {
                await body(dispatcher);
                await Drain(dispatcher);
            }
            finally
            {
                dispatcher.UnhandledException -= Backstop;
                handler.Detach(dispatcher);
            }

            void Backstop(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                if (!e.Handled) missed.Add(e.Exception);
                e.Handled = true;
            }
        });

        Assert.Empty(missed);
    }

    /// <summary>
    /// Queues work the way a click or a binding does. BeginInvoke rather than InvokeAsync: an
    /// InvokeAsync operation keeps its exception in its task, so it would never go unhandled. And
    /// typed as Action, which the dispatcher calls directly - any other delegate goes through
    /// DynamicInvoke and arrives wrapped in a TargetInvocationException.
    /// </summary>
    private static void Post(Dispatcher dispatcher, Action action, DispatcherPriority priority = DispatcherPriority.Normal) =>
        dispatcher.BeginInvoke(priority, action);

    /// <summary>Lets everything already queued run, including the dialogs the handler defers.</summary>
    private static async Task Drain(Dispatcher dispatcher)
    {
        // Twice: the first pass runs the failures, whose handling queues the dialogs for the second.
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    /// <summary>For failures that land after thread-pool work, which no queue position can predict.</summary>
    private static async Task Until(Dispatcher dispatcher, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The failure never reached the crash handler.");
            await Task.Delay(10);
        }

        await Drain(dispatcher);
    }
}
