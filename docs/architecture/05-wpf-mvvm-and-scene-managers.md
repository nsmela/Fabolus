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

## The View Lifecycle Contract: `IViewState` ([`IViewState.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/IViewState.cs))

When the operator switches tabs across the top navigation bar (e.g. from `meshes` to `smooth` to `mould`), the application does not destroy and recreate ViewModel state. Instead, ViewModels implement the [`IViewState`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/IViewState.cs) lifecycle interface:

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
2. **`DeactivateAsync()`**: Invoked when the user navigates away. The ViewModel releases cached heavy meshes, instructs its Scene Manager to clear visual elements, and yields back the updated `Workspace` to [`MainViewModel`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Main/MainViewModel.cs).

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

### The Contract: `ISceneManager` ([`ISceneManager.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Viewport/ISceneManager.cs))
```csharp
public interface ISceneManager
{
    event Action<Element3D> VisualAddedOrUpdated;
    event Action<Guid> VisualRemovedById;
    event Action VisualsCleared;

    void OnActivated();
    void OnDeactivated();

    // Input the viewport forwards to the active manager.
    bool OnKeyDown(Key key);
    bool OnKeyUp(Key key);
    bool OnMouseDown(MouseDown3DEventArgs eventArgs);
    bool OnMouseUp(MouseUp3DEventArgs eventArgs);
    bool OnMouseMove(IList<HitTestResult> hits);
}
```

Concrete scene managers additionally expose a `ReleaseMesh()` method (used when a feature is deactivated) by convention; it is not part of the interface.

The [`ViewportControl`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Viewport/ViewportControl.xaml.cs) binds to the active ViewModel's `SceneManager`. When geometry or colouring changes:
1. The Scene Manager converts the domain mesh (optionally with per-vertex colours) into a HelixToolkit `MeshGeometry3D` via [`MeshConverters.ToHelixMesh`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Common/Mesh/MeshConverters.cs).
2. It wraps the geometry in an `Element3D` and fires `VisualAddedOrUpdated`, which `ViewportControl` adds to or updates in the scene.
3. The ViewModel itself holds no `Element3D` references and can be unit-tested without a graphical window.

---

## Inter-Component Event Messaging (`IMessenger`)

Fabolus uses CommunityToolkit's `WeakReferenceMessenger` to broadcast system events across loosely coupled components without introducing circular references:

| Message Contract | Emitter | Subscriber(s) | Operational Purpose |
| :--- | :--- | :--- | :--- |
| `WorkspaceChangedMessage` | ViewModels | Feature ViewModels | Broadcasts the current workspace so listeners can resync from active mesh state. |
| `IsLoadingMessage` | Heavy Workflows | `LoadingOverlay` | Activates the non-blocking progress spinner over the 3D viewport during computation. |
| `PreferenceSectionUpdateMessage<T>` | `PreferencesViewModel` | `AppPreferencesStore` | Applies a saved preferences section and persists it to disk. |
| `PreferenceSectionRequestMessage<T>` | ViewModels | `AppPreferencesStore` | Request/reply pattern fetching a preferences section without singleton coupling. |
| `CaptureScreenshotMessage` | `MainViewModel` | `ViewportControl` | Instructs the viewport to render an offscreen frame to disk. |
