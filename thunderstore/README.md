# Ship Customizer

Adds a **CUSTOM** loadout to the hub's ship loadout terminal. Fit any weapon or module, of any tier, on every hardpoint of
your ship, choose what it starts with on its storage racks, and pick the homunculus. A sandbox for trying out builds the
regular loadouts never offer.

Works on every ship (Frigate, Striker, Destroyer), on a monitor or in VR.

## How to use

1. In the hub, go to the **ship loadout terminal** and pick your ship.
2. Choose **CUSTOM** at the bottom of the LOADOUTS list. It starts as a copy of ROOKIE.
3. Change the ship:
   - **Click a hardpoint** on the ship picture (a **+** marks a free one) to get every weapon and module that fits it,
     with rarity colours and the game's tooltips. A 2x1 hardpoint takes one 2x1 module or two 1x1 modules.
   - **HARDPOINTS** lists every hardpoint as a big row (port or starboard, bow to aft), handy in VR.
   - **STORAGE** opens every storage rack: click a slot to put any item there (ammo, power cells, repair plates,
     materials, animus crates, weapon crates, mods or relics).
   - **HOMUNCULUS** picks the standard homunculus or any Blessed Homunculus, each with what its blessing does.
   - **FABRICATOR** sets how many alloys the run starts with, and the fabricator's level (MK I to MK III).
   - **SAVE LOADOUT** keeps the loadout on your PC (10 slots per ship) to bring back in any later match.
   - **LOAD LOADOUT** brings back a saved loadout, starts from any of the game's loadouts (challenge ones included), or
     empties the hardpoints, the racks, or everything.
4. Press **SELECT** and start a mission as usual.

Your CUSTOM loadouts are saved per ship and kept between sessions.

## Multiplayer and the modding rules

Ship Customizer follows Void Crew's official modding guidelines for sandbox mods:

- **Every player with Ship Customizer can choose CUSTOM and change it**, and **everyone in the room needs Ship
  Customizer** for it. If someone without the mod joins while CUSTOM is chosen, the room goes back to ROOKIE. Nobody is
  kicked.
- Rooms you host are marked **[mod_session]** in the server list. Players need "Can join [mod_session] games" turned on to
  join them.
- **A CUSTOM run gives nobody XP, rewards, quest progress or achievements**, and the room is closed to new players until
  the CUSTOM mission is over. A saved CUSTOM run stays without progression when you continue it.
- The CUSTOM loadouts are the host's: everyone sees and changes them, and the changes are saved on the host's PC. With a
  host on 0.4.0 or older, only the host can.
- During a CUSTOM run the storage racks take any item.

## Works with Void Crew Fleet

With Void Crew Fleet installed, its second ship can use CUSTOM too. Under SHIP 2 the terminal shows ship 2's CUSTOM
loadout: the same as ship 1's until you change it there. LOAD LOADOUT has SAME AS SHIP 1 to go back. Choose CUSTOM with
SELECT under SHIP 2 for ship 2 to use it; left AS SHIP 1, ship 2 starts as a copy of ship 1. Ship 2's fabricator gets its
own level and, with Void Crew Fleet 0.2.1 or newer, its own starting alloys (with an older Fleet, both ships start with
ship 1's). Any player can set ship 2 up, and the same rules apply to both ships.

In a **Fleet Battle** (PvP), ship 1 belongs to the BLUE team and ship 2 to the RED team: only a ship's own team can change
its CUSTOM loadout, and the other team sees it view-only.

**Match rules keep a Fleet Battle even:**

- A CUSTOM ship never starts with relics, the Armory Exchange Key, extra Fabricator modules or the game's unfinished
  Placeholder weapons; the menus leave them out. Relics found during the match are yours to use.
- With Void Crew Fleet 0.3.0 or newer, the host sets the rest on Fleet's lobby screen (SHIP RULES): tiers, categories,
  single items, the fabricator's highest level and the most starting alloys. The menus grey out what the rules refuse.
- **MATCH RULES**, above the CUSTOM section, shows the rules and what breaks them, and can take it off. Anything that
  still breaks them comes off when the mission starts (your saved loadouts keep everything), and everyone is told in the
  chat.

Without Void Crew Fleet (not installed, or turned off in its config), all of this stays off and Ship Customizer works on
its own.

## Settings

`BepInEx\config\com.captainchunck.voidcrew.shipcustomizer.cfg`:

| Setting | Default | What it does |
|---|---|---|
| General > Enabled | true | Turn the mod off without removing it. |
| Rules > AllowAnySize | false | Let any module go in any hardpoint, whatever its size. Big modules in small hardpoints can stick through walls. |
| Rules > AnyItemOnAnyRack | true | Any item goes in any storage rack slot, in the CUSTOM editor and during a CUSTOM run. Big items can stick out of small slots. |
| Rules > CopyPresetModuleContents | true | A module that a game loadout also uses starts with the same contents (loaded ammo crate, power cell). |
| Rules > ShowQuestItems | false | List quest items in the storage picker. They can confuse mission objectives. |
| Diagnostics > WriteGameDataReport | true | Writes everything the mod found to `ShipCustomizer\game-data.txt` in the game's save folder, for bug reports. |
| Diagnostics > VerboseLogging | false | Extra detail in the BepInEx log. |

## Known limits

- The pop-up lists work with the mouse (or a VR pointer); gamepad navigation isn't supported yet.
- In VR the terminal pointer can drift from where clicks land when you turn your head, so small targets such as the
  hardpoint icons are hard to hit. Use **HARDPOINTS** instead.
- The first time the terminal opens, the mod reads every module and weapon from the game, which takes a second or two.

## Source and bug reports

Source code, manual downloads and issues: https://github.com/CaptainChunck/VoidCrewShipCustomizer

When reporting a bug, please attach `BepInEx\LogOutput.log` and `ShipCustomizer\game-data.txt` from
`%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew`.
