# Air Channels

Air channels are tubes added to the mould so that, when it is later cast, silicone can enter and air can leave. They are placed in the **mould** tab on the mould preview and subtracted from the mould when you generate it. The casting itself is done outside Fabolus.

---

## Channel types

Fabolus provides three channel types (`AirChannelType`):

- **Straight** — a tube running straight up from the mesh surface.
- **Angled** — leaves the surface perpendicular to the wall, then curves upward (useful on steep walls, where a straight tube would leave a thin edge).
- **Painted** — a channel drawn by dragging along the surface, following a path (useful along a ridge).

<!-- IMAGE_PLACEHOLDER: [Figure 6.1: Straight, Angled, and Painted channels on a bolus mesh.] -->

---

## Channel parameters

The channel controls in the mould tab include:

| Control | Default | What it does |
| :--- | :--- | :--- |
| **Channel Diameter** | `5.0 mm` | Diameter of the main tube. |
| **Tip Diameter** | `3.0 mm` | Diameter at the tip where it meets the mesh. |
| **Tip Length** | `3.0 mm` | Length of the tapered tip. |
| **Tip Depth** | `1.0 mm` | How far the tip penetrates into the mesh. |

A default channel diameter and the automatic placement settings below are stored in `PrintBedPreferences` (see [Configuration & Preferences](../reference/configuration-and-preferences.md)).

---

## Placing channels

Air channels must be placed before generating the mould:

1. In the **mould** tab, choose a channel type.
2. Move the cursor over the mesh; a preview follows the cursor.
3. Click to place a Straight or Angled channel, or drag to draw a Painted channel.
4. Select a placed channel to adjust its parameters or remove it.

Placed channels are stored with the mould definition, so they are subtracted from the shell when you click **Generate Mould** and are preserved in the saved project.

<!-- IMAGE_PLACEHOLDER: [Figure 6.2: Placing a channel, with the live preview following the cursor.] -->

---

## Automatic placement

When the mould is poured, silicone rises through the cavity and pushes air up ahead of it. Every local high point of the bolus surface traps a bubble unless a channel vents it. Fabolus can find these pockets and place a channel at each one.

- **Show air pockets** (on by default) puts an amber marker on the bolus at every pocket no channel vents yet. Markers disappear as you vent them, and come back if you delete the channel that vented them.
- **Auto-place channels** (in the channels panel) adds an **Angled** channel at every pocket that isn't already vented. Channels already placed are kept, and the new ones can be selected, edited or deleted like any other.
- With **Generate air channels automatically** turned on in Preferences (the default), this also runs each time the mould tab opens and no channels have been placed yet.

Auto-placed channels use the **default channel diameter** from Preferences, not the values currently in the panel. On a flat top, the channel goes in the middle rather than at an edge.

Two preferences control which pockets get a channel:

| Preference | Default | What it does |
| :--- | :--- | :--- |
| **Minimum pocket depth** | `1.0 mm` | How far below its peak a pocket has to hold air before it could spill over towards somewhere higher. Raise it to ignore small bumps. The highest point of the bolus always counts. |
| **Minimum channel spacing** | `6.0 mm` | Pockets closer than this share one channel, at the higher of them. No new channel is placed this close to an existing one. |

An existing channel only vents a pocket if it enters within the minimum pocket depth of the peak. A channel lower down is covered by silicone before the air above it can escape, so that pocket still gets a channel of its own.
