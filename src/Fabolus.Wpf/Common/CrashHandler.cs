using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Fabolus.Wpf.Common;

/// <summary>
/// Last line of defence for exceptions nothing else caught. Expected failures come back as Result
/// errors and are shown where they happen; this is for the ones that throw - a geometry bug, a
/// null reference - which used to close the app with no word as to why.
/// </summary>
/// <remarks>
/// A UI-thread exception is logged, marked handled so the app keeps running, and shown to the
/// user. That covers async commands and async void handlers too: both rethrow on the dispatcher.
/// A background-thread exception cannot be recovered from, so that one is only logged and shown
/// before the process ends. An unobserved task exception is only logged - nothing was waiting
/// on it, so there is nothing to tell the user.
/// </remarks>
public sealed class CrashHandler {
    public const string UiThreadContext = "UI thread (recovered)";
    public const string BackgroundThreadContext = "Background thread (fatal)";
    public const string UnobservedTaskContext = "Unobserved task";
    public const string StartupContext = "Startup (fatal)";

    private readonly CrashLog _log;
    private readonly IAlertDialog _alert;

    // Failures the user has already been told about. A throw from something that runs every frame
    // or every drag step would otherwise put a dialog in front of them as fast as they close it.
    private readonly HashSet<string> _notified = [];
    private readonly object _gate = new();
    private bool _showing;

    public CrashHandler(CrashLog log, IAlertDialog alert) {
        _log = log;
        _alert = alert;
    }

    /// <summary>Catches whatever goes unhandled on <paramref name="dispatcher"/>'s thread.</summary>
    public void Attach(Dispatcher dispatcher) =>
        dispatcher.UnhandledException += OnDispatcherUnhandledException;

    public void Detach(Dispatcher dispatcher) =>
        dispatcher.UnhandledException -= OnDispatcherUnhandledException;

    /// <summary>Background-thread and unobserved-task exceptions, for the whole process.</summary>
    public void AttachProcessWide() {
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public void DetachProcessWide() {
        AppDomain.CurrentDomain.UnhandledException -= OnFatalException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }

    public void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e) {
        var path = _log.Write(e.Exception, UiThreadContext);
        e.Handled = true;

        // Shown on a later dispatcher turn rather than from inside the event: the exception may have
        // been raised mid-layout or mid-render, where the dispatcher is suspended and a modal dialog
        // cannot open.
        var exception = e.Exception;
        e.Dispatcher.InvokeAsync(() => Notify(exception, path, fatal: false));
    }

    public void OnFatalException(object? sender, UnhandledExceptionEventArgs e) {
        var exception = e.ExceptionObject as Exception
            ?? new Exception($"Non-exception object thrown: {e.ExceptionObject}");

        // Log first: the process is going down, and the dialog may never get to show.
        var path = _log.Write(exception, BackgroundThreadContext);
        Notify(exception, path, fatal: e.IsTerminating);
    }

    public void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
        _log.Write(e.Exception, UnobservedTaskContext);
        e.SetObserved();
    }

    /// <summary>
    /// For a failure before the main window is up. The caller shuts down after this returns - with
    /// no window there is nothing to keep running.
    /// </summary>
    public void ReportStartupFailure(Exception exception) {
        var path = _log.Write(exception, StartupContext);
        Notify(exception, path, fatal: true);
    }

    private void Notify(Exception exception, string? logPath, bool fatal) {
        lock (_gate) {
            // One dialog at a time. A modal dialog pumps messages, so a second failure can arrive
            // while the first is still on screen; it is logged, and that is enough. A fatal one
            // always shows - it is the last thing the user will see of this session.
            if (!fatal && (_showing || !_notified.Add(Signature(exception))))
                return;

            _showing = true;
        }

        try {
            _alert.ShowError(Message(exception, logPath, fatal, _log.Redactor));
        }
        catch (Exception) {
            // Nothing more can be done from here, and throwing would end the app this is keeping up.
        }
        finally {
            lock (_gate) _showing = false;
        }
    }

    public static string Message(Exception exception, string? logPath, bool fatal, CrashRedactor redactor) {
        var lead = fatal
            ? "Fabolus ran into an unexpected problem and has to close."
            : "Something went wrong and the last action did not finish. Fabolus is still running, "
              + "but it is worth exporting your work before carrying on.";

        var where = logPath is null
            ? "The details could not be saved to a log file."
            : $"The details were saved to:\n{logPath}";

        // Redacted like the log: a screenshot of this is as likely to end up on an issue.
        return $"{lead}\n\n{exception.GetType().Name}: {redactor.Redact(exception.Message)}\n\n{where}";
    }

    // Same type, message and stack is the same failure, however many times it repeats.
    private static string Signature(Exception exception) =>
        $"{exception.GetType().FullName}|{exception.Message}|{exception.StackTrace}";
}
