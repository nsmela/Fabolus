using System.Windows;

namespace Fabolus.Wpf.Features.Bootstrapping;

// Identity 1: Used for the lightweight, framework-dependent build
public class ManagedWpfSplashScreen : Window, ISplashScreenRole {
    public ManagedWpfSplashScreen() { /* Initialize XAML */ }

    public void Reveal() => Show();
    public void Conceal() => Close();
}

// Identity 2: Used for the self-contained build
public class NativeInteropSplashScreen : ISplashScreenRole {
    public void Reveal() {
        // Do nothing. The unmanaged C++ launcher already painted the image 
        // to the screen before the .NET runtime even booted.
    }

    public void Conceal() {
        // Signal the unmanaged process to destroy its window and terminate
        if (EventWaitHandle.TryOpenExisting("Fabolus_Splash_Dismiss", out var waitHandle)) {
            waitHandle.Set();
        }
    }
}