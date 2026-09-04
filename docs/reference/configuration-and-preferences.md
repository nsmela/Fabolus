# Configuration & Preferences Reference

Fabolus provides a centralized, strongly-typed preferences subsystem in `Fabolus.Wpf.Features.AppPreferences`. Settings are stored locally in the user's application data folder and synchronized across ViewModels via `AppPreferencesStore` and CommunityToolkit `IMessenger`.

---

## Preference Keys Reference ([`PreferenceKeys.cs`](file:///c:/Users/nsmel/Documents/Programming/Fabolus/src/Fabolus.Wpf/Features/AppPreferences/AppPreferences.cs#L69))

| Key Name | Data Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| `ImportFolder` | `string` | `CommonDocuments` | Default directory opened when launching the file import dialog. |
| `ExportFolder` | `string` | `LocalApplicationData` | Default output destination for exported 3MF and STL files. |
| `ExportFormat` | `ExportFormat` enum | `Stl` | Default file format selected in the Export view (`Stl` or `ThreeMf`). |
| `PrintbedWidth` | `float` | `250.0 mm` | Width ($X$) of the 3D printer build plate rendered in the viewport. |
| `PrintbedDepth` | `float` | `250.0 mm` | Depth ($Y$) of the 3D printer build plate rendered in the viewport. |
| `PrintbedHeight` | `float` | `300.0 mm` | Height ($Z$) bounding envelope of the 3D printer. |
| `ShowBedGrid` | `bool` | `true` | Toggles the visibility of the millimeter grid on the virtual print bed. |
| `AutodetectChannels` | `bool` | `true` | Automatically computes optimal surface normal vectors when clicking to place vents. |
| `ChannelDiameter` | `float` | `4.0 mm` | Default diameter for newly placed straight and angled vent channels. |
| `AccentColor` | `string` | `#FF0CA3B4` | Primary brand accent color used in UI highlighting and title bar accents. |
| `ViewportBackground`| `ViewportBackground`| `Graphite` | Viewport shading preset (`Graphite`, `DarkSlate`, `StudioLight`). |
| `Units` | `MeasurementUnit` | `Millimeters` | Unit of measure displayed across the status bar and tool panels. |
| `EnableCut` | `bool` | `false` | **Feature Flag**: Enables the experimental planar cutting tool for multi-part moulds. |
| `EnableSplit` | `bool` | `false` | **Feature Flag**: Displays the Split View tab in the top navigation bar. |

---

## Modifying Preferences in the UI

1. Click the **Gear icon** in the top-right application caption bar to open the **Preferences Window**.
2. Select a category from the left sidebar:
   - **General**: Default import/export folder paths and default file export format.
   - **Print Bed**: Set custom build plate dimensions matching your department's 3D printer (e.g., Bambu Lab X1/P1 $256\times 256\times 256$, Prusa MK4 $250\times 210\times 220$).
   - **Air Channels**: Default channel diameter and auto-detection behavior.
   - **Appearance**: Select accent theme swatches and 3D viewport background gradient.
   - **Experimental**: Toggle cutting and mould splitting tools.
3. Preferences save automatically upon window closure and broadcast an `AppPreferencesChangedMessage` to immediately update active 3D scenes.
