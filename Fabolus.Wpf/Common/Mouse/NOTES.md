# Notes for Mouse Binding
## References
- https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.SharpDX.Shared/Viewport/ViewportCoreProperties.cs#L390
- https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/Examples/WPF.SharpDX/ExampleBrowser/Examples/CursorPosition/MainWindow.xaml#L128
- https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/HelixToolkit.UWP.Shared/Controls/MouseHandlers/ViewportCommand.cs
- https://github.com/helix-toolkit/helix-toolkit/blob/3e3f7527b10028d5e81686b7e6d82ef3aac11a37/Source/Examples/WPF.SharpDX/MouseDragDemo/InteractionHandle3D.cs#L52


## Method
- Create a "ViewPortCommand" and send it via Messaging
- each handlers has a static implementation?
- the mesh itself handles the interaction? mousing over the mesh enables actions/events? not the mode?