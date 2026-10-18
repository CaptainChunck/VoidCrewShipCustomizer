# Changelog

## 0.4.0

- Works with **Void Crew Fleet**: its second ship can use CUSTOM too, with a layout of its own.
  - Under SHIP 2 the terminal shows and edits ship 2's CUSTOM loadout. It is the same as ship 1's until you change it
    there; COPY A LOADOUT has **SAME AS SHIP 1** to go back, and **SHIP 1'S CUSTOM** to copy it once.
  - The same rules as ship 1: only the host chooses it, everyone in the room needs Ship Customizer, and a CUSTOM ship 2
    makes the run a CUSTOM run (no progression, room closed to new players).
  - Other players with the mod see the host's ship 2 CUSTOM loadout.
- A public API for other mods (`ShipCustomizerApi`, API version 1).
- With Ship Customizer alone, nothing changes.

## 0.3.0

- Follows Void Crew's official modding guidelines for sandbox mods:
  - Rooms you host are marked **[mod_session]** from the start, so players opt in before they join.
  - **Everyone in the room needs Ship Customizer** for CUSTOM. If someone without it joins in the hub, CUSTOM switches
    back to ROOKIE (nobody is kicked).
  - **Nobody earns progression in a CUSTOM run**, and the room is closed to new players until the CUSTOM mission is over.
  - A saved CUSTOM run can only be continued while everyone has the mod.
- Important notices also appear in your text chat, so you see them away from the loadout terminal too.

## 0.2.0

- CUSTOM is built on ROOKIE (the game's default loadout) instead of a hidden challenge loadout.
- New **HARDPOINTS** button: every hardpoint as a big row, easy to pick in VR.
- The CUSTOM buttons moved above SELECT, clear of the panel headers.
- COPY A LOADOUT lists the terminal's loadouts and the challenge loadouts on separate tabs.
- Empty hardpoints in game loadouts no longer cause warnings.

## 0.1.0

- First test build: CUSTOM loadout, clickable hardpoints, storage-rack editor, homunculus picker, copy and empty.
