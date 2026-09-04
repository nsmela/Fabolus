# WPF MVVM & Scene Managers

## MVVM with .NET CommunityToolkit

The user interface of Fabolus is built using modern **WPF on .NET 8**, leveraging the **CommunityToolkit.Mvvm** framework. Through compile-time Roslyn source generators, boilerplate code is eliminated while maintaining high performance and zero reflection overhead:

```csharp
public partial class SmoothingViewModel : ObservableObject, IViewState 
{
    [ObservableProperty] private int _iterations = 1;
    [ObservableProperty] private float _intensity = 1.5f;

    [RelayCommand]
    private async Task ApplySmoothingAsync() {
        ...
    }
}
```

The generator automatically expands this into standard `INotifyPropertyChanged` notification boilerplate, `Iterations` public property accessors, and asynchronous `IRelayCommand` wrappers.

---

## The View Lifecycle Contract: `IViewState` ([`IViewState.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/IViewState.cs))

When the operator switches tabs across the top navigation bar (e.g. from `meshes` to `smooth` to `mould`), the application does not destroy and recreate ViewModel state. Instead, ViewModels implement the [`IViewState`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/IViewState.cs) lifecycle interface:

```csharp
public interface IViewState
{
    ISceneManager SceneManager { get; }
    Task ActivateAsync(Workspace workspace);
    Task<Workspace> DeactivateAsync();
}
```

<!-- IMAGE_PLACEHOLDER: [Figure 14.2: Feature View Lifecycle State Transition. Flowchart showing navigation from MeshManager through Smoothing, Rotation, and Moulding via IViewState.ActivateAsync and DeactivateAsync. Dimensions: 800x400px.] -->

1. **`ActivateAsync(workspace)`**: Invoked when the tab becomes active. The ViewModel ingests the current workspace state, synchronizes parameters, and instructs its Scene Manager to render feature-specific visuals (e.g. turning on rotation gizmos or cutting planes).
2. **`DeactivateAsync()`**: Invoked when the user navigates away. The ViewModel releases cached heavy meshes, instructs its Scene Manager to clear visual elements, and yields back the updated `Workspace` to [`MainViewModel`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Main/MainViewModel.cs).

---

## Decoupling the 3D Viewport: The `ISceneManager` Pattern

A critical software engineering innovation in Fabolus is the strict separation between ViewModel state and 3D graphics rendering.

<!-- IMAGE_PLACEHOLDER: [Figure 14.1: Viewport Decoupling Architecture. Component interaction diagram showing MainViewModel, Feature ViewModels, ISceneManager implementations, ViewportControl, and HelixToolkit DirectX 11 pipeline. Dimensions: 900x500px.] -->

```
┌──────────────────────────────────────┐           ┌──────────────────────────────────────┐
│          Feature ViewModel           │           │        Feature Scene Manager         │
│  - Parameters, Commands, Workflows   │ ──calls──>│  - DirectX 11 Buffers & Shaders      │
│  - Zero DirectX / WPF dependencies   │           │  - Translates Domain -> Element3D    │
└──────────────────────────────────────┘           └──────────────────┬───────────────────┘
                                                                      │ raises events
                                                                      ▼
                                                   ┌──────────────────────────────────────┐
                                                   │           ViewportControl            │
                                                   │    (HelixToolkit.Wpf.SharpDX)        │
                                                   └──────────────────────────────────────┘
```

### The Problems with Coupling ViewModels to 3D Elements
- HelixToolkit 3D models (`MeshGeometryModel3D`, `DiffuseMaterialCore`, `LineGeometryModel3D`) are heavy DirectX 11 graphical resources bound to the WPF UI thread.
- If ViewModels hold references to `Element3D` objects, unit testing without launching an entire graphical desktop window becomes impossible.
- DirectX memory leaks easily arise when visual objects are held alive by ViewModel data bindings.

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

The [`ViewportControl`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/Viewport/ViewportControl.xaml.cs) binds directly to the active ViewModel's `SceneManager`. When geometry or display modes change:
1. The ViewModel tells the Scene Manager: `"SetDisplayMode(SmoothDisplayMode.Heatmap)"`.
2. The Scene Manager converts the domain mesh's vertices and signed distance floats into DirectX vertex color buffers via [`MeshConverters.ToMeshGeometry3D`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Common/Mesh/MeshConverters.cs).
3. The Scene Manager fires `VisualAddedOrUpdated`, passing the DirectX model to `ViewportControl`.
4. The ViewModel remains 100% free of UI imports and can be fully unit-tested with standard mocks.

---

## Inter-Component Event Messaging (`IMessenger`)

Fabolus uses CommunityToolkit's `WeakReferenceMessenger` to broadcast system events across loosely coupled components without introducing circular references:

| Message Contract | Emitter | Subscriber(s) | Operational Purpose |
| :--- | :--- | :--- | :--- |
| `WorkspaceChangedMessage` | ViewModels | `MainViewModel`, `InfoPanelViewModel` | Synchronizes active bolus names, status bar indicators, and physical statistics. |
| `IsLoadingMessage` | Heavy Workflows | `LoadingOverlay.xaml` | Activates non-blocking progress spinner over the 3D viewport during computation. |
| `AppPreferencesChangedMessage` | `PreferencesViewModel` | `ViewportControl`, Features | Updates virtual print bed boundaries, grid dimensions, and rendering accent colors. |
| `CaptureScreenshotMessage` | `MainViewModel` | `ViewportControl` | Instructs the DirectX swap chain to render an offscreen render target to disk. |
| `AppPreferencesRequest` | ViewModels | `AppPreferencesStore` | Synchronous request-reply pattern fetching user preferences without singleton coupling. |
