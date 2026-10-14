using System.Runtime.CompilerServices;
using System.Windows;

// So the tests can cover internals that are deliberately not part of the app's surface -
// the preference section roster and the migration off the old exe config.
[assembly: InternalsVisibleTo("Fabolus.Wpf.Tests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
