# Changelog

## 0.7.0

- **The HOMUNCULUS menu shows what each Blessed Homunculus's blessing does**: the text of the game's own tooltip, with
  its highlights, so you don't have to hover over each one. Blessed Homunculi in the storage-rack list show it too.
- **Match rules keep a Fleet Battle (Void Crew Fleet's PvP) even:**
  - In every Fleet Battle, a CUSTOM ship never starts with relics, the Armory Exchange Key, extra Fabricator modules or
    the game's unfinished Placeholder weapons (nor their crates, nor the Placeholder weapon mod). The menus leave them
    out, and the storage list has no RELICS tab. Relics found during the match are yours to use.
  - With Void Crew Fleet 0.3.0 or newer, the host sets the rest on Fleet's SHIP RULES tab:
    - which tiers, categories and single items CUSTOM may use. Tiers are MK I to MK III; items go by rarity, and
      Legendary ones count as the top tier. Banning a weapon bans its weapon crate too;
    - the fabricator's highest level (MK I is always allowed);
    - the most starting alloys.

    The menus grey out what the rules refuse: NOT ALLOWED IN THIS MATCH.
  - **MATCH RULES**, above the CUSTOM section, shows the rules and anything in the loadout that breaks them (the button
    then reads BREAKS MATCH RULES), with a one-click TAKE OFF. Storage tiles that break the rules are marked NOT ALLOWED.
  - When the mission starts, the host takes off whatever still breaks the rules, from a copy, so your saved loadouts
    keep everything. Ship 2's starting alloys are limited too. Every player with Ship Customizer is told in the chat what
    came off.
- For Void Crew Fleet: a new API member, `GetRuleOptions` (ApiVersion stays 1).
- The game-data report lists each homunculus's blessing and the match-rule options.

## 0.6.0

- **SAVE LOADOUT** and **LOAD LOADOUT**:
  - Keep a CUSTOM loadout on your PC (10 slots per ship) and bring it back in any later match, as the host or as
    another player.
  - LOAD LOADOUT also has the game's loadouts (what COPY LOADOUT did) and the EMPTY options.
- The pop-up windows (the pickers and the storage racks) are built like the game's own pop-ups: its dark-grey window
  with the light-blue outline and wings, the LOADOUTS list's rows, and its buttons. In a Fleet Battle the outline takes
  the team's colour.
- **Storage racks start with exactly what you chose.**
  - The CUSTOM ship gets fresh rack entries (nothing left over from the loadout it's built on).
  - Right after it spawns, the host checks every rack slot and puts back the chosen item if a slot holds anything else.
    The log names any slot it had to fix.
  - With Void Crew Fleet, ship 2 uses its own CUSTOM only when CUSTOM is chosen (SELECT) under SHIP 2; set to AS SHIP 1,
    it starts as a copy of ship 1.

## 0.5.1

- With Void Crew Fleet, ship 2's FABRICATOR menu has ALLOYS too, so ship 2 (the RED team's ship in a Fleet Battle) can
  start with alloys of its own. It needs a Void Crew Fleet that supports this. With an older Fleet, the menu shows the
  level only, and both ships start with ship 1's alloys as before.
- For Void Crew Fleet: a new API member, `GetShip2Alloys` (ApiVersion stays 1).

## 0.5.0

- **Every player with Ship Customizer can choose CUSTOM and change it**, not only the host.
  - Everyone sees and changes the host's CUSTOM loadouts, and the changes are saved on the host's PC. COPY LOADOUT has
    **YOUR OWN CUSTOM** to bring in the one saved on your PC.
  - With Void Crew Fleet, any player can set up and choose ship 2's CUSTOM too.
  - Needs the host on 0.5.0 or newer. With an older host, only the host can, as before.
- **Storage racks take any item in any slot**, in the CUSTOM editor and during a CUSTOM run (setting AnyItemOnAnyRack).
- New **FABRICATOR** button: the alloys a run starts with, and the fabricator's level (MK I to MK III). With Void Crew
  Fleet, ship 2's fabricator gets its own level, and both ships start with ship 1's alloys.
- **Void Crew Fleet's Fleet Battle (PvP)**: ship 1 belongs to the BLUE team and ship 2 to the RED team, and only a ship's
  own team can change its CUSTOM loadout (the host too); the other team's is view-only. The CUSTOM pop-ups take the
  team's colour.
- Without Void Crew Fleet (not installed, or turned off in its config), everything for a second ship stays off and Ship
  Customizer works on its own.
- The CUSTOM buttons are now a section in the terminal's own style, level with DESCRIPTION and HOMUNCULUS.
- The modding-guideline rules are unchanged: everyone in the room needs Ship Customizer for CUSTOM, CUSTOM runs give no
  progression, and the room is closed to new players during a CUSTOM mission.

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
