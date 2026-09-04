# WPF MVVM & Scene Managers

## MVVM with CommunityToolkit

The presentation layer in `Fabolus.Wpf` is built with **.NET CommunityToolkit.Mvvm**, leveraging source generators for clean, boilerplate-free ViewModels:
- `[ObservableProperty]` generates backing fields, INotifyPropertyChanged events, and property-changed handlers.
- `[RelayCommand]` generates WPF ICommand implementations with async execution guards.

---

## Decoupling the 3D Viewport: The `ISceneManager` Pattern

One of the most critical architectural patterns in Fabolus is the decoupling of 3D DirectX graphics from ViewModel state.

```
┌─────────────────────────────────┐           ┌─────────────────────────────────┐
│        Feature ViewModel        │           │      Feature Scene Manager      │
│   (State, Commands, Logic)      │ ──calls──>│  (Materials, Meshes, Gizmos)    │
└─────────────────────────────────┘           └────────────────┬────────────────┘
                                                               │ binds to
                                                               ▼
                                              ┌─────────────────────────────────┐
                                              │         ViewportControl         │
                                              │  (HelixToolkit.Wpf.SharpDX)     │
                                              └─────────────────────────────────┘
```

### Why Decouple Viewport Logic?
- HelixToolkit 3D models (`MeshGeometryModel3D`, `DiffuseMaterialCore`, `LineGeometryModel3D`) are heavy DirectX 11 resources tied to the WPF UI thread.
- If ViewModels construct or hold references to `Element3D` objects, unit testing becomes impossible without launching a full WPF window and graphics context.
- Memory leaks occur easily if DirectX textures and buffers are retained by long-lived ViewModels.

### The Contract: `ISceneManager` ([`ISceneManager.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Viewport/ISceneManager.cs))
```csharp
public interface ISceneManager
{
    event EventHandler<VisualElementArgs> VisualAddedOrUpdated;
    event EventHandler<string> VisualRemovedById;
    event EventHandler VisualsCleared;
    void ReleaseMesh();
}
```

Every feature implements its own specialized Scene Manager:
- [`MeshManagerSceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/MeshManager/MeshManagerSceneManager.cs): Renders loaded meshes, wireframes, and bounding boxes.
- [`SmoothingSceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Smoothing/SmoothingSceneManager.cs): Manages signed distance vertex coloring, ghost transparency, and cutting plane manipulators.
- [`RotateSceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Rotatation/RotateSceneManager.cs): Manages 3D rotation gizmos and dynamic overhang vertex gradient coloring.
- [`MouldSceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Moulding/MouldSceneManager.cs): Renders transparent mould shells, raycast hit-testing for channel placement, and live air channel curves.
- [`ExportSceneManager`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Export/ExportSceneManager.cs): Clean final visualization of the export package.

---

## Inter-Component Messaging (`IMessenger`)

Fabolus uses CommunityToolkit's `WeakReferenceMessenger` for decoupled communication across features:

| Message Type | Sender | Receiver | Purpose |
| :--- | :--- | :--- | :--- |
| `WorkspaceChangedMessage` | ViewModels | `MainViewModel`, Viewport | Notifies all components of updated workspace geometry. |
| `IsLoadingMessage` | Features | `LoadingOverlay` | Activates/deactivates the non-blocking progress spinner during heavy compute. |
| `AppPreferencesChangedMessage` | `PreferencesViewModel` | Features, Viewport | Broadcasts updated bed sizes, accent colors, or folder paths. |
| `CaptureScreenshotMessage` | `MainViewModel` | `ViewportControl` | Triggers a high-resolution viewport render to clipboard or disk. |
