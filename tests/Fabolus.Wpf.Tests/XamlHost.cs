using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Fabolus.Wpf.Tests;

/// <summary>
/// One STA thread, with one WPF <see cref="Application"/> and the app's theme dictionaries, shared
/// by every test that loads real XAML.
/// </summary>
/// <remarks>
/// Application.Current is a process-wide singleton and belongs to the thread that created it, so
/// each test class cannot set one up for itself: whichever class runs first wins the singleton,
/// and the rest find an Application owned by a thread that has usually already exited. Their
/// resource lookups then fail cross-thread, and which class is the unlucky one changes from run to
/// run. Everything goes through this one thread instead, so there is exactly one Application and
/// it is always the thread asking.
/// </remarks>
public static class XamlHost
{
    private static readonly Lazy<Dispatcher> Host = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Same dictionaries App.xaml merges, so resources resolve as they do live.</summary>
    private static readonly string[] ThemeDictionaries = [
        "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
        "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
        "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Buttons.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Colours.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Controls.xaml",
        "pack://application:,,,/Fabolus;component/Themes/Icons.xaml",
        "pack://application:,,,/Fabolus;component/Themes/SteelSlider.xaml",
        "pack://application:,,,/Fabolus;component/Themes/SteelCyan.xaml",
    ];

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>();

        var thread = new Thread(() =>
        {
            try
            {
                // A pack URI resolves its assembly by simple name through Assembly.Load, which
                // only finds one already in the load context. Touch a type first so the Fabolus
                // theme dictionaries can be found.
                _ = typeof(Fabolus.Wpf.Features.Smoothing.SmoothingView).Assembly;

                if (Application.Current is null)
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var source in ThemeDictionaries)
                    {
                        app.Resources.MergedDictionaries.Add(
                            new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) });
                    }
                }

                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception e)
            {
                ready.SetException(e);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,   // the run should not be held open by this thread
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="action"/> on the host thread and rethrows whatever it threw.</summary>
    public static void Run(Action action)
    {
        Exception? failure = null;

        Host.Value.Invoke(() =>
        {
            try { action(); }
            catch (Exception e) { failure = e; }
        });

        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(
                $"{failure.GetType().Name}: {failure.Message}{Environment.NewLine}{failure}");
        }
    }
}
