using System.Windows.Threading;

namespace Fabolus.Wpf.Tests;

/// <summary>
/// Runs a body on an STA thread with a running dispatcher, the way the app runs it.
/// </summary>
/// <remarks>
/// Needed by anything that exercises a view's own threading rather than just its state. These
/// views touch thread-affine WPF objects and await work they pushed onto the thread pool, so
/// without a dispatcher the continuation resumes on a pool thread and the scene managers throw.
/// A DispatcherTimer also only ticks while its dispatcher is pumping, so any test about
/// coalescing or debouncing has to run here.
/// </remarks>
public static class UiThread
{
    public static void Run(Func<Task> body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Generous: a rebuild on a large mesh is most of a second, and a hang here should fail
        // the test rather than wedge the run.
        if (!thread.Join(TimeSpan.FromMinutes(2)))
        {
            throw new TimeoutException("The UI-thread body did not finish within two minutes.");
        }

        if (failure is not null)
        {
            throw new Exception("Body threw on the UI thread.", failure);
        }
    }
}
