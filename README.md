<p align="center"><img src="thunderstore/icon.png" width="160" alt="Ship Customizer icon"></p>

# Void Crew Ship Customizer

A BepInEx mod for **Void Crew** that adds a **CUSTOM** loadout to the hub's ship loadout terminal. Fit any weapon or
module, of any tier, on every hardpoint of your ship, choose what it starts with on its storage racks, and pick the
homunculus: a sandbox for builds the regular loadouts never offer.

Works on every ship (Frigate, Striker, Destroyer), on a monitor or in VR.

## Download

- **Thunderstore** (recommended): install **Ship Customizer** for Void Crew with r2modman, Gale or the Thunderstore Mod
  Manager. BepInEx is installed for you.
- **Manual**: download `VoidCrewShipCustomizer.dll` from the
  [Releases](https://github.com/CaptainChunck/VoidCrewShipCustomizer/releases) page and put it in your BepInEx `plugins`
  folder (see [Manual install](#manual-install)).

## What it does

- **A CUSTOM entry** at the bottom of the LOADOUTS list, for every ship. It starts as a copy of ROOKIE.
- **Clickable hardpoints.** Every free hardpoint on the ship picture shows a **+**. Click a hardpoint (empty or not) for a
  list of every weapon and module that fits it, from Mk I to Mk III, with rarity colours and the game's own tooltips,
  sorted into WEAPONS, DEFENSE and SUPPORT tabs.
  - A 2x1 hardpoint takes one 2x1 module, or two 1x1 modules side by side, as in the game.
  - The game's "only one of these per ship" rule still applies (marked ONE PER SHIP).
- **Buttons above SELECT** while CUSTOM is shown:
  - **HARDPOINTS**: every hardpoint as a big row (port or starboard, bow to aft, with what is fitted). Easy to use in VR.
  - **STORAGE**: every storage rack and slot. Choose ammo, power cells, repair plates, materials, animus crates, weapon
    crates (an inscribed crate that builds that weapon on any free hardpoint), weapon mods or relics. Items a rack
    refuses are greyed out.
  - **HOMUNCULUS**: the standard homunculus, or any Blessed Homunculus from the game's loadouts.
  - **COPY A LOADOUT**: start from any of the game's loadouts, including the challenge ones, then change what you like.
  - **EMPTY**: clear the hardpoints, the racks, or everything.
- Your CUSTOM loadouts are saved per ship in
  `%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew\ShipCustomizer\custom-loadouts.json`.

## Multiplayer and the modding rules

Ship Customizer follows Void Crew's
[official modding guidelines](https://steamcommunity.com/app/1063420/discussions/0/6057990917770747901/) for sandbox mods:

- **Only the host** chooses CUSTOM, and **everyone in the room needs Ship Customizer** for it. If someone without the mod
  joins while CUSTOM is chosen, the room goes back to ROOKIE (nobody is kicked).
- Rooms you host are marked **[mod_session]** in the server list, the game's tag for mods that change the session.
  Players need "Can join [mod_session] games" turned on to join them.
- **A CUSTOM run gives nobody XP, rewards, quest progress or achievements**, from the moment the mission starts until
  everyone is back in the hub. The room is closed to new players during a CUSTOM mission. A saved CUSTOM run stays
  without progression when you continue it, and can only be continued while everyone has the mod.
- Other players with the mod see the host's CUSTOM loadout on their own terminal (view only).

## Manual install

You need BepInEx 5 for Void Crew (the Thunderstore BepInExPack, or BepInEx 5.4.23 from GitHub). VR players using Rai Pal
already have it.

1. Download `VoidCrewShipCustomizer.dll` from [Releases](https://github.com/CaptainChunck/VoidCrewShipCustomizer/releases).
2. Copy it into your BepInEx `plugins` folder:
   - Plain BepInEx: `...\steamapps\common\Void Crew\BepInEx\plugins`
   - Rai Pal (VR): `%APPDATA%\raicuparta\rai-pal\data\installed-mods\<number>\bepinex\BepInEx\plugins` (the number is on
     the `target_assembly` line of `Void Crew\doorstop_config.ini`).
3. Start the game. `BepInEx\LogOutput.log` should say
   `All game patches are in; the CUSTOM loadout is available at the ship loadout terminal.`

To remove it, delete the DLL. Your saved CUSTOM loadouts stay in the save folder and do nothing without the mod.

## Settings

`BepInEx\config\com.captainchunck.voidcrew.shipcustomizer.cfg` (created on first start):

| Setting | Default | What it does |
|---|---|---|
| General > Enabled | true | Turn the mod off without removing it. |
| Rules > AllowAnySize | false | Let any module go in any hardpoint, whatever its size. Big modules in small hardpoints can stick through walls. |
| Rules > CopyPresetModuleContents | true | A module that one of the game's loadouts also uses starts with the same contents (loaded ammo crate, power cell). |
| Rules > ShowQuestItems | false | List quest items in the storage picker. They can confuse mission objectives. |
| Diagnostics > WriteGameDataReport | true | The first time you open the terminal, write everything the mod found to `ShipCustomizer\game-data.txt` in the save folder. |
| Diagnostics > VerboseLogging | false | Extra detail in the BepInEx log. |

## Known limits

- The pop-up lists work with the mouse (or a VR pointer); gamepad navigation isn't supported yet.
- In VR the terminal pointer can drift from where clicks land when you turn your head, so small targets such as the
  hardpoint icons are hard to hit. Use **HARDPOINTS** instead; its rows are large.
- The first time the terminal opens, the mod reads every module and weapon from the game, which takes a second or two.
- Crates sitting unbuilt on a hardpoint in a game loadout are not copied by COPY A LOADOUT.

## Bug reports

Please open an [issue](https://github.com/CaptainChunck/VoidCrewShipCustomizer/issues) and attach `BepInEx\LogOutput.log`
and `ShipCustomizer\game-data.txt` from `%USERPROFILE%\AppData\LocalLow\Hutlihut Games ApS\Void Crew`.

## Building from source

Open `VoidCrewShipCustomizer.slnx` in Visual Studio 2026 (or run MSBuild) and build **Release**. The project references
the game's and BepInEx's DLLs from your install (they are not in this repository); if yours are elsewhere, change
`VoidCrewDir` and `BepInExDir` at the top of `VoidCrewShipCustomizer\VoidCrewShipCustomizer.csproj`.

- `tools\package.ps1` builds Release and writes the Thunderstore upload to `dist\VoidCrewShipCustomizer-<version>.zip`.
- `tools\make-icon.ps1` redraws `thunderstore\icon.png`.

## How it works (for modders)

- Each ship gets a `ShipLoadoutDataDef` of its own, backed by a runtime `ShipLoadoutData` registered with the game's
  `RuntimeAssetsRegister` (effect None, so joining players aren't required to have it). The terminal lists, draws and
  describes it with its own code.
- A CUSTOM loadout is built on the game's default loadout (`ShipLoadoutDataContainer.GetDefault()`, ROOKIE) when that is
  for the same ship, otherwise on the lowest-rank loadout the terminal lists. The game's challenge and test loadouts are
  never used for this.
- Picking CUSTOM on the host selects that loadout in its place (`HubShipManager.SelectShip`), so no player ever receives
  an unknown loadout id. Modded players learn about CUSTOM through the room property `SC_Custom`; `SC_Run` marks a CUSTOM
  mission and `SC_Closed` a room closed for one. Each modded player sets the player property `SC_Player`.
- When the mission starts (`GSIngame.TransitionTo`), the host swaps the session's `ToLoadShipData` for a `ShipLoadout`
  built from the CUSTOM choices, and the game's own `ShipLoadout.InitializeShip` spawns it for everyone. Anything the game
  could not spawn is left out with a warning instead of stopping the mission from starting.
- Progression is paused by skipping `PlayerRewardsCollection.AddItem`, achievements and quest progress during CUSTOM
  runs. Every Harmony patch is looked up by name and applied on its own, so a game update turns CUSTOM off instead of
  breaking the game.
