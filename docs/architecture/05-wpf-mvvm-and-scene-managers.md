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

### Practical Example: Linking `SmoothingViewModel` and `SmoothingSceneManager`

To see this decoupling in action, consider how smoothing operations and cross-sections are displayed in the 3D viewport:

#### 1. The ViewModel Owns the Scene Manager and Sends Domain Data
The ViewModel ([`SmoothingViewModel.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Smoothing/SmoothingViewModel.cs)) implements `IViewState`. It manages user inputs (such as smoothing intensity or display mode) and passes pure domain meshes (`IMesh`) to its scene manager, without needing to know anything about DirectX 3D visual elements:

```csharp
public partial class SmoothingViewModel : ObservableObject, IViewState 
{
    private readonly SmoothingSceneManager _sceneManager;
    private IMesh? _stagedMesh;

    // Expose the scene manager to satisfy the IViewState contract
    public ISceneManager SceneManager => _sceneManager;

    public SmoothingViewModel(IGeometryEngine engine, IMessenger messenger)
    {
        // Instantiates its dedicated scene manager
        _sceneManager = new SmoothingSceneManager(engine, messenger);
    }

    private void RenderViewport()
    {
        if (_stagedMesh is null) return;

        // Prepare domain meshes (pure geometry, zero DirectX dependencies)
        IMesh? unsmoothedMesh = _unsmoothedTwin;
        double[]? heatmapColors = ComputeHeatmapColors();

        // Hand domain meshes to the Scene Manager for display
        _sceneManager.UpdateMesh(_stagedMesh, unsmoothedMesh, heatmapColors);
    }
}
```

#### 2. The Scene Manager Builds 3D Visuals and Fires Events
The Scene Manager ([`SmoothingSceneManager.cs`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Smoothing/SmoothingSceneManager.cs)) receives the domain meshes, converts them into graphical geometry, configures DirectX materials, and raises events:

```csharp
public class SmoothingSceneManager : ISceneManager
{
    public event Action<Element3D>? VisualAddedOrUpdated;
    public event Action<Guid>? VisualRemovedById;

    private Guid _activeVisualId = Guid.Empty;

    public void UpdateMesh(IMesh mesh, IMesh? unsmoothedMesh = null, double[]? heatmapColors = null)
    {
        // 1. Remove the previous visual from the 3D scene
        VisualRemovedById?.Invoke(_activeVisualId);

        // 2. Convert domain mesh to HelixToolkit DirectX geometry
        MeshGeometry3D geometry = mesh.ToHelixMesh(_engine, heatmapColors).Value;

        // 3. Create the 3D visual element and assign materials
        var model = new MeshGeometryModel3D
        {
            Geometry = geometry,
            Material = _displayMode == SmoothDisplayMode.Heatmap 
                ? new VertColorMaterial() 
                : Skins.Surface.Emerald,
            CullMode = SharpDX.Direct3D11.CullMode.Back,
        };
        _activeVisualId = model.GUID;

        // 4. Notify the ViewportControl to display the new visual
        VisualAddedOrUpdated?.Invoke(model);
    }
}
```

#### 3. The Viewport Binds to the Active Scene Manager
In [`MainView.xaml`](https://github.com/nsmela/Fabolus/blob/v1/src/Fabolus.Wpf/Features/Main/MainView.xaml), the shared 3D viewport binds directly to whichever ViewModel is currently active:

```xml
<viewport:ViewportControl 
    Grid.Row="1"
    SceneManager="{Binding SceneManager}" />
```

When `MainViewModel` switches tabs, `ViewportControl` automatically swaps event subscriptions:
1. Unsubscribes from the previous scene manager's events and calls `oldManager.OnDeactivated()`.
2. Subscribes to the incoming scene manager's events (`VisualAddedOrUpdated`, `VisualRemovedById`, `VisualsCleared`) and calls `newManager.OnActivated()`.
3. When `VisualAddedOrUpdated` fires, `ViewportControl` adds the new `Element3D` to the HelixToolkit rendering pipeline.

#### 4. Forwarding Interactive Viewport Input
When operators interact directly with 3D elements in the viewport (such as dragging a cutting plane gizmo or picking channel coordinates), `ViewportControl` forwards mouse and keyboard events to the active `ISceneManager`:

```csharp
// Inside ViewportControl.xaml.cs
private void OnMouseDown(object sender, MouseButtonEventArgs e)
{
    if (SceneManager is not null)
    {
        var hits = MainViewport.FindHits(e.GetPosition(MainViewport));
        SceneManager.OnMouseDown(new MouseDown3DEventArgs(hits, e));
    }
}
```

If the interaction changes clinical state (such as adjusting a cross-section height or placing an air vent), the Scene Manager notifies the ViewModel via a callback, keeping presentation and domain logic cleanly separated.

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
