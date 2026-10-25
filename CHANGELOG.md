# Changelog

## 0.9.4

- **Saves are tried again:** if a virus scanner or backup tool holds the save file for a moment, your last CUSTOM changes
  are no longer lost. Ship Customizer tries again a few seconds later, and a few times in a row before a mission and as
  the game closes. A CUSTOM run that couldn't be written is no longer forgotten, so continuing it later still keeps
  progression paused.
- **Safer after game updates:** CUSTOM now turns off if an update stops Ship Customizer from pausing any kind of
  progression (achievements, quest progress, stats and Steam stats, daily and weekly quests, the start of a saved CUSTOM
  run), not only rewards. The log says which kind.

## 0.9.3

- **Setting up a ship together works at the terminal.** Players' changes did reach the host, but every other terminal
  kept showing what it showed before (often ROOKIE), so players looked at different loadouts and never saw each other's
  CUSTOM changes.
  - Every player's terminal now shows what the room has chosen for the ship it sets up, and moves along when anyone
    picks another loadout or CUSTOM. A player looking at another loadout isn't moved; starting to use the terminal shows
    a pick made meanwhile.
  - Open CUSTOM lists (HARDPOINTS, a hardpoint's choices, CONTENTS, HOMUNCULUS, FABRICATOR, a rack slot's items, MATCH
    RULES) update when someone else changes the loadout, and a pick made after that counts against what the list shows.
    Two changes to the same part at the same moment still keep the first one, and the other player is told.
  - Just after joining, the terminal says WAITING FOR HOST until the host's CUSTOM loadouts arrive. Before, a change made
    then only went into your own file.
  - With Void Crew Fleet, changing a ship's CUSTOM that the ship doesn't use yet (AS SHIP 1) tells you to SELECT it for
    that ship.
  - The host applies players' last changes right before the mission starts.
  - Joining no longer shows "NAME changed CUSTOM ..." for changes made before you joined.
- **Drag to copy in STORAGE:** press on a filled rack slot, hold, and drag it onto another slot (on the same rack or
  another one) to copy its item there, replacing what was there. A click still opens the list. While you drag, the slot
  under the item lights up, or turns red with the reason when it won't take it, and the list scrolls at its edges. A copy
  is checked and shared like a pick. Works with a mouse and a VR laser.
- **Storage items that looked doubled:**
  - The game itself has two different items with the same name for several payload batteries (Cluster Missiles, Energy
    Missile, Heavy Torpedo, Medium Missile, Proximity Mine). The lists now say what tells them apart.
  - An item a list has twice is shown once.
  - A mod battery's inner payload (Ectype Skirmisher's micromissile payload, named like its battery) is left out: it isn't
    an item, and a rack that started with it held a loose warhead.
- **Modules stuck after taking one apart** (left in the slot, switched off for good): that was Void Crew Fleet, not Ship
  Customizer, and Void Crew Fleet 0.4.5 fixes it. A made-up share code can no longer put a weapon's core on a hardpoint
  without its weapon data, which would have got stuck the same way.
- Players on 0.9.2 can still play with 0.9.3 (no network change); they don't get the terminal following the room's
  choice or the lists updating.

## 0.9.2

- **Ready for Void Crew Fleet 0.4.2:** when Fleet refuses a request to change ship 1's loadout (in a Fleet Battle, from a
  player of the other team), Ship Customizer no longer switches CUSTOM on or off for it. Only a tampered game could
  send such a request.

## 0.9.1

A full code review, with fixes and speed-ups.

- **Progression in CUSTOM runs (the modding rules):**
  - Leaving a CUSTOM mission early (quitting to the main menu, following an invite, being disconnected) no longer lets
    that mission's stats and Steam stats through. The game saves them while it closes the mission, after the room is
    gone, so the pause now holds until the hub or another mission.
  - A run flag left behind by an older host can't leave a later CUSTOM run unpaused.
  - If a game update ever breaks the hub hook that ends a run's pause, CUSTOM is switched off instead of leaving
    progression paused.
- **Saving:**
  - The save files are on the disk before they replace the old ones, so a power cut can't empty them.
  - A damaged file is kept as `.broken`, and the copy from before the last save is used instead.
  - Changes are saved a second after the last one (quick picks are saved together), and straight away before a mission
    and when the game closes.
  - What a newer version saves is kept when this one saves.
- **Several players changing one ship:**
  - A player's change was refused every time when the host's own loadout held something the host's checks would take
    out (a module a game update removed, or a setting turned off since). Fixed.
  - A quick second change to the same part is no longer refused as a clash with yourself.
  - After SAME AS SHIP 1, the next change to that ship works, and a refused change no longer stops a ship following ship
    1.
  - Two players fitting the same one-per-ship module at once can't give the ship two.
  - A refused change to a ship that follows ship 1 now reaches its player.
- **Speed:**
  - Loading the hub no longer reads every module and item, which took up to about a second and a half. CUSTOM is built
    when the terminal first shows it.
  - Other players' edits redraw the terminal once, not several times. The hub's ship hologram is only rebuilt when the
    room's choice changes.
  - The game-data report is only written again after an update of the mod or the game.
  - Hovering list rows, long item lists and joining a room cost less.
- **The terminal:**
  - A message about a refused pick is no longer hidden behind the list that opens again. "Loaded" and "Saved" only show
    when it worked.
  - The second click of a double-click no longer picks a row in the window it just opened.
  - Hover sounds on the hardpoints no longer pile up.
  - The CLEAR confirmation opens on the CLEAR tab.
  - A pick from a list made for another ship is refused instead of landing on the wrong ship.
  - A damaged or made-up share code gives a message instead of nothing.
  - Several hub messages at once are all shown.

## 0.9.0

- **Up to 6 CUSTOM ships with Void Crew Fleet 0.4.0**: up to 3 ships in co-op, and up to 3 a team in a Fleet Battle.
  - Every ship has its own CUSTOM layout, fabricator level and starting alloys, set up under that ship on the terminal.
  - In co-op a ship follows ship 1's CUSTOM until it is changed. LOAD LOADOUT can copy any other ship's CUSTOM once.
  - In a Fleet Battle the odd ships are BLUE's and the even ships RED's. Only a ship's own team can change it.
  - No ship follows or copies the other team's CUSTOM. An unchanged ship starts from its type's default loadout.
    Before, RED's ship 2 showed BLUE's ship 1 CUSTOM until it was changed.
  - When the host makes a ship a given type, only that type's CUSTOM is offered for it, and other types are refused.
  - CUSTOM on ships 3 to 6 needs everyone in the room on 0.9.0 or newer; the reason names who isn't.
    Ships 1 and 2 keep working with older versions.
  - Ships that copy ship 1 in co-op get its fabricator level and contents. They are recognised as the very ship 1 loadout,
    so a plain ROOKIE ship is never mistaken for a copy of an unchanged CUSTOM ship.
- **Void Crew Fleet's features only apply when the host runs Fleet too.** Before, a player with Fleet could see Fleet
  features in a room whose host doesn't run it. Now, without Fleet on the host, Ship Customizer works exactly as it does
  without Fleet: ship 1 only, no team or match rules, nothing of Fleet's on the terminal.
- For other mods: `GetDefaultLoadout`, `CheckShip`, `SetShip`, `BuildShip`, `GetShipAlloys` and `ShipCustomCancelled`
  take ships 2 to 6. The ship 2 members stay.

## 0.8.0

- **Several players can change the same ship at once.**
  - Changes are made part by part: each hardpoint, each storage rack slot, the homunculus, the alloys and the
    fabricator level. Changes to different parts all stay.
  - When two players change the same part at the same moment, the first change stays and the other player is told who
    got there first, instead of one change silently replacing the other.
  - A pick from a list whose part changed while the list was open isn't made either, and everyone sees who changed the
    loadout.
  - Players on 0.7 and older can still play with a 0.8.0 host; their changes work as before.
- **CONTENTS** for any fitted weapon or module: choose what it starts loaded with.
  - Ammo crates or batteries for a weapon (whatever its type takes), weapon mods, power cells for shields and charge
    stations, and payloads for the payload launcher.
  - Only what fits each slot is listed. USUAL CONTENTS keeps what the game's loadouts load it with.
- **Power check**: every weapon and module shows the power it draws (generators: what they make). The HARDPOINTS list,
  the hardpoint lists and the CUSTOM description show the ship's total: "Power: 18 of 24 made".
- **Share codes**: COPY AS A SHARE CODE (in SAVE LOADOUT) puts the whole loadout on the clipboard as one line of text.
  PASTE A SHARE CODE (in LOAD LOADOUT, and LOAD RACKS) brings one in; only what fits the ship comes in.
- **Progression in CUSTOM runs, as the modding rules ask:**
  - Tracked stats, Steam stats and daily and weekly quests are now paused too. Before, a daily quest could be marked
    done in a CUSTOM run and its reward lost.
  - The pause holds when the host leaves mid-run, and the new host remembers the run as a CUSTOM run.
  - A saved CUSTOM run only starts once everyone in the room has the mod.
  - The pause ends when you leave the room, not only back in the hub.
- **Fixes:**
  - With Void Crew Fleet:
    - each ship's fabricator starts at its own level, whichever team the host is on;
    - a ship 2 left on "as ship 1" gets ship 1's level;
    - the host's changes to ship 1 no longer reach RED's ship 2.
  - Some MK III weapons no longer start with weapon mods copied from the game's test loadouts. Usual contents come only
    from the loadouts the terminal lists, and never include mods.
  - Changes from other players (and share codes) are checked part by part: only real modules that fit, real items, a
    real homunculus, and this PC's settings (AnyItemOnAnyRack, AllowAnySize).
  - The save files are written in one step (a crash can't lose them), and a file that can't be read is never
    overwritten. Damaged loadout data no longer causes errors.
  - SELECT no longer shows SELECTED when CUSTOM was refused.
  - "CUSTOM was switched off" messages go to every modded player's chat.
  - Rare errors can no longer start a CUSTOM ship with progression on, or block the hub.
- **Menus:**
  - FABRICATOR has 250 alloys, and always lists the current amount and a Fleet Battle's limit.
  - HARDPOINTS is numbered and comes back after each pick.
  - Players who can only look at a ship get read-only lists, and the storage window follows team changes while open.
  - Replacing or clearing a saved slot asks for a second click.
  - MATCH RULES rows go straight to the problem.
  - The pop-up lists reuse their rows, so opening many of them no longer builds up memory until you leave the hub.
- The game-data report lists each weapon's and module's power, item slots and usual contents, and each ship's own power.

## 0.7.1

- Long item names on the storage-rack tiles are readable again. They get smaller text and show from their first line,
  so "Heavy Caliber: Large Crate" no longer shows only "Caliber:".
- The NOT ALLOWED mark on a storage tile no longer runs into two-digit slot numbers.
- A pop-up with tabs highlights the tab it opens on straight away, not only after you click a tab.

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
