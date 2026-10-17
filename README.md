# Void Crew Ship Customizer

A BepInEx plugin for **Void Crew** that adds a **CUSTOM** loadout to the hub's ship loadout terminal. You choose what is
fitted on every hardpoint of your ship (any weapon or module, any tier), what the ship starts with on its storage racks,
and which homunculus sits in the Central Computer Cradle.

> Status: **0.1.0, first test build.** It compiles and passes the offline checks, but it has not been tried in the game
> yet. Please test it in a private game first.

## What it does

- **A CUSTOM entry** at the bottom of the LOADOUTS list, for every ship (Frigate, Striker, Destroyer and any added later).
- **Clickable hardpoints.** With CUSTOM shown, every free hardpoint on the ship picture shows a **+**. Click a hardpoint
  (empty or not) to get a list of every weapon and module that fits it, from Mk I to Mk III, with rarity colours and the
  game's own tooltips. Tabs sort the list into WEAPONS, DEFENSE and SUPPORT.
  - A 2x1 hardpoint takes either one 2x1 module, or two 1x1 modules side by side, as in the game. Picking a 1x1 module on
    an empty 2x1 hardpoint puts it in one half; the other half then shows its own **+**.
  - The game's rule of "only one of these per ship" still applies (marked ONE PER SHIP).
  - "EMPTY THIS HARDPOINT" takes a module off again.
- **A toolbar** at the top-left of the ship picture:
  - **STORAGE**: every storage rack and slot. Click a slot to choose an item: ammo, power cells, repair plates,
    materials, animus crates, weapon mods, relics, and **weapon crates** (an inscribed crate that builds that weapon on
    any free hardpoint). Items a rack refuses are greyed out.
  - **HOMUNCULUS**: the standard homunculus, or any Blessed Homunculus from the game's loadouts.
  - **COPY A LOADOUT**: start from a copy of one of the game's loadouts (ROOKIE, CQB, HEAVY, ...), then change what you
    like.
  - **EMPTY**: clear the hardpoints, the racks, or everything.
- Your CUSTOM loadouts are saved per ship in
  `%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew\ShipCustomizer\custom-loadouts.json`, so they survive
  restarts and BepInEx reinstalls.

## Multiplayer and fair play

- **Only the host can choose CUSTOM.** The host's ship is the one that spawns, so it is the host's CUSTOM loadout that
  counts. Other players with the mod see the host's CUSTOM loadout on their terminal (view only); players without the mod
  see the ship's first loadout (e.g. ROOKIE) as chosen, and get the custom ship when the mission starts. Nobody else needs
  the mod to play.
- **Games with a CUSTOM loadout are marked [mod_session]** in the server list, using the game's own flag for modded
  sessions, so players know before they join. They need "Join modded games" turned on to join.
- **Progression is paused for CUSTOM runs.** The Void Crew modding rules say sandbox-style mods must turn progression off,
  so a run started with a CUSTOM loadout gives players who have this mod no XP, rewards, quest progress or achievements,
  from the moment the mission starts until they are back in the hub. Rewards from earlier runs can still be claimed.
  A saved run that was started with CUSTOM stays paused when you continue it.
- CUSTOM is not offered for loadout challenges or when continuing a saved run (the game fixes the ship for those).

## Install

You need BepInEx 5 for Void Crew. If you play in VR with Rai Pal, its BepInEx is already there.

1. Download `VoidCrewShipCustomizer.dll` (or build it, see below).
2. Copy it into your BepInEx `plugins` folder:
   - Rai Pal (VR): `%APPDATA%\raicuparta\rai-pal\data\installed-mods\<number>\bepinex\BepInEx\plugins`
     (the number is in `Void Crew\doorstop_config.ini`, on the `target_assembly` line).
   - Plain BepInEx: `...\steamapps\common\Void Crew\BepInEx\plugins`.
3. Start the game. The BepInEx log (`BepInEx\LogOutput.log`) should say
   `All game patches are in; the CUSTOM loadout is available at the ship loadout terminal.`

To remove it, delete the DLL. Your saved CUSTOM loadouts stay in the save folder and do nothing without the plugin.

## Settings

`BepInEx\config\com.captainchunck.voidcrew.shipcustomizer.cfg` (created on first start):

| Setting | Default | What it does |
|---|---|---|
| General > Enabled | true | Turn the plugin off without removing it. |
| Rules > AllowAnySize | false | Let any module go in any hardpoint, whatever its size. Big modules in small hardpoints can stick through walls. |
| Rules > CopyPresetModuleContents | true | A module that one of the game's loadouts also uses starts with the same contents (loaded ammo crate, power cell). |
| Rules > ShowQuestItems | false | List quest items in the storage-rack picker. They can confuse mission objectives. |
| Diagnostics > WriteGameDataReport | true | The first time you open the terminal, write everything the plugin found to `ShipCustomizer\game-data.txt` in the save folder. |
| Diagnostics > VerboseLogging | false | Extra detail in the BepInEx log. |

## Known limits of 0.1.0

- Mouse only: the pop-up lists don't take gamepad navigation yet.
- The first time the terminal opens, the plugin reads every module and weapon from the game, which can take a second or two.
- Modules start empty unless one of the game's loadouts also uses them (see CopyPresetModuleContents).
- Crates sitting unbuilt on a hardpoint in a game loadout are not copied by COPY A LOADOUT.

## Build

Open `VoidCrewShipCustomizer.slnx` in Visual Studio 2026 (or run MSBuild) and build **Release**. The project references
the game's and BepInEx's DLLs from your install; if yours are elsewhere, change `VoidCrewDir` and `BepInExDir` at the top
of `VoidCrewShipCustomizer\VoidCrewShipCustomizer.csproj`. The output is
`VoidCrewShipCustomizer\bin\Release\VoidCrewShipCustomizer.dll`.

## How it works (for modders)

- Each ship gets a `ShipLoadoutDataDef` of its own, backed by a runtime `ShipLoadoutData` registered with the game's
  `RuntimeAssetsRegister` (effect None, so joining players aren't required to have it). The terminal lists, draws and
  describes it with its own code.
- Picking CUSTOM on the host selects the ship's first loadout in its place (`HubShipManager.SelectShip`), so no other
  player ever receives an unknown loadout id. The custom choice reaches modded players through the room property
  `SC_Custom`; `SC_Run` marks a mission started with CUSTOM.
- When the mission starts (`GSIngame.TransitionTo`), the host swaps the session's `ToLoadShipData` for a `ShipLoadout`
  built from the CUSTOM choices. The game's own `ShipLoadout.InitializeShip` spawns it, so every player sees the same ship.
- Anything the game could not spawn (e.g. an item removed by a game update) is left out with a warning instead of
  stopping the mission from starting.
