using System;
using System.IO;
using System.Linq;
using System.Text;
using CG.Client.Utils;
using CG.Ship.Hull;
using HarmonyLib;
using ResourceAssets;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Writes ShipCustomizer\game-data.txt: every ship, hardpoint, storage rack, game loadout, module, weapon and item
    /// the customizer found, plus the terminal's screen layout. Useful for bug reports and for checking a game update.
    /// </summary>
    internal static class GameDataReport
    {
        private const int MaxLayoutLines = 600;

        public static void Write(VisualElement terminalRoot)
        {
            string path = Path.Combine(LoadoutStore.Folder, "game-data.txt");
            try
            {
                var text = new StringBuilder();
                text.AppendLine(Plugin.Name + " " + Plugin.Version + " game-data report");
                text.AppendLine("Written " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " | game " + Application.version + " | Unity " + Application.unityVersion);
                text.AppendLine();

                text.AppendLine("== SHIPS ==");
                foreach (GUIDUnion shipGuid in Catalog.ShipGuids())
                {
                    HullInfo hull;
                    if (!Catalog.TryGetHull(shipGuid, out hull))
                    {
                        text.AppendLine("(could not read ship " + shipGuid + ")");
                        continue;
                    }
                    WriteHull(text, hull);
                }

                text.AppendLine("== WEAPONS AND MODULES (" + Catalog.SlotOptions.Count + ") ==");
                foreach (Option option in Catalog.SlotOptions)
                {
                    text.AppendLine(string.Format("  {0,-9} {1,-4} {2,-10} {3}  {4}{5}", option.Category, option.HasSize ? Catalog.SizeName(option.Size) : "?",
                        Ui.RarityName(option.Rarity), option.Name, option.Code, option.SingleInstance ? "  (one per ship)" : ""));
                }
                text.AppendLine();

                text.AppendLine("== STORAGE ITEMS (" + Catalog.ItemOptions.Count + ") ==");
                foreach (Option option in Catalog.ItemOptions)
                {
                    text.AppendLine(string.Format("  {0,-14} {1,-10} {2}  {3}", option.Category, Ui.RarityName(option.Rarity), option.Name, option.Code));
                }
                text.AppendLine();

                text.AppendLine("== HOMUNCULUS VARIANTS IN GAME LOADOUTS ==");
                foreach (Option option in Catalog.HomunculusOptions) text.AppendLine("  " + option.Name + "  " + option.Code);
                text.AppendLine();

                text.AppendLine("== TERMINAL SCREEN LAYOUT ==");
                if (terminalRoot != null)
                {
                    int lines = 0;
                    WriteTree(text, terminalRoot, 0, ref lines);
                }
                else
                {
                    text.AppendLine("  (terminal not available)");
                }

                Directory.CreateDirectory(LoadoutStore.Folder);
                File.WriteAllText(path, text.ToString());
                Log.Info("Wrote the game-data report to " + path + ".");
            }
            catch (Exception e)
            {
                Log.Failure("Writing the game-data report to " + path, e);
            }
        }

        private static void WriteHull(StringBuilder text, HullInfo hull)
        {
            text.AppendLine(hull.Name + "  [" + hull.ShipGuid + "]");
            text.AppendLine("  Loadouts, lowest rank first (the first stands in for CUSTOM with other players):");
            foreach (ShipLoadoutDataDef preset in hull.Presets)
            {
                text.AppendLine("    " + LoadoutStore.PresetName(preset) + "  [" + preset.AssetGuid + "]");
            }

            text.AppendLine("  Hardpoint positions (" + hull.Sockets.Count + " in " + hull.GroupCount + " hardpoints):");
            foreach (SocketInfo socket in hull.Sockets)
            {
                text.AppendLine(string.Format("    #{0,-2} {1}  hardpoint {2}  overlaps [{3}]  cells {4}", socket.Index, Catalog.SizeName(socket.Size), socket.Group + 1,
                    string.Join(",", socket.Overlaps.Select(i => i.ToString()).ToArray()),
                    string.Join(" ", socket.Cells.Select(c => "(" + c.x.ToString("0.#") + "," + c.y.ToString("0.#") + "," + c.z.ToString("0.#") + ")").ToArray())));
            }

            text.AppendLine("  Storage racks:");
            foreach (ShelfInfo shelf in hull.Shelves)
            {
                CarryablesSocket first = shelf.Slots.FirstOrDefault();
                text.AppendLine(string.Format("    {0}: core system #{1} [{2}] {3} slots; accepts {4}; rejects {5}", shelf.Name, shelf.CoreIndex, shelf.ModuleGuid, shelf.Slots.Count,
                    first != null ? Describe(first.accepted) : "?",
                    first != null && first.rejectedList != null && first.rejectedList.Length > 0 ? string.Join(", ", first.rejectedList.Select(Describe).ToArray()) : "nothing"));
            }

            text.AppendLine("  Game loadouts' contents:");
            foreach (ShipLoadoutDataDef preset in hull.Presets)
            {
                try
                {
                    ShipLoadout loadout = preset.Asset.ShipLoadout;
                    text.AppendLine("    " + LoadoutStore.PresetName(preset) + ":");
                    foreach (ShipSocketEntry socket in loadout.Sockets)
                    {
                        Option option = Catalog.OptionFor(socket);
                        int stocked = socket.Upgrades != null ? socket.Upgrades.Count(u => u != null && !u.IsNull) : 0;
                        text.AppendLine(string.Format("      hardpoint #{0}: {1} ({2}){3}", socket.SocketId, option != null ? option.Name : "?", option != null ? option.Code : socket.Module.AssetGuid.ToString(),
                            stocked > 0 ? ", stocked with " + string.Join(", ", socket.Upgrades.Where(u => u != null && !u.IsNull).Select(u => NameOfItem(Catalog.CodeFor(u))).ToArray()) : ""));
                        if (socket.BuildBox != null && !socket.BuildBox.IsNull) text.AppendLine("      hardpoint #" + socket.SocketId + " also has a crate: " + socket.BuildBox.AssetGuid);
                    }
                    foreach (ShipSocketEntry core in loadout.CoreSystemSockets)
                    {
                        if (core.Upgrades == null || core.Upgrades.All(u => u == null || u.IsNull)) continue;
                        text.AppendLine(string.Format("      core system #{0} ({1}): {2}", core.SocketId, Catalog.HeaderOf(Catalog.ContextOf(core.Module.AssetGuid), core.Module.AssetGuid.ToString()),
                            string.Join(", ", core.Upgrades.Select(u => u == null || u.IsNull ? "-" : NameOfItem(Catalog.CodeFor(u))).ToArray())));
                    }
                    if (loadout.HomunculusOverride != null && !loadout.HomunculusOverride.IsNull)
                    {
                        text.AppendLine("      homunculus: " + NameOfItem("i:" + loadout.HomunculusOverride.AssetGuid.AsHex()));
                    }
                    if (loadout.AdditionalAssets != null && loadout.AdditionalAssets.Count > 0)
                    {
                        text.AppendLine("      extra loose items: " + loadout.AdditionalAssets.Count);
                    }
                }
                catch (Exception e)
                {
                    text.AppendLine("      (could not read: " + e.Message + ")");
                }
            }
            text.AppendLine();
        }

        private static string NameOfItem(string code)
        {
            Option option;
            return Catalog.TryGetOption(code, out option) ? option.Name : code;
        }

        private static string Describe(CsObjectReference reference)
        {
            if (reference == null) return "?";
            try
            {
                Traverse t = Traverse.Create(reference);
                object method = t.Field("compareMethod").GetValue();
                string detail = "";
                if (method != null && method.ToString() == "Type")
                {
                    object type = t.Field("type").Property("Type").GetValue();
                    detail = " " + (type != null ? type.ToString() : "?");
                }
                else if (method != null && method.ToString() == "Prefab")
                {
                    var asset = t.Field("orbitObjectRef").GetValue() as ResourceAssetRef;
                    detail = " " + (asset != null ? NameOfItem("i:" + asset.AssetGuid.AsHex()) : "?");
                }
                return (method != null ? method.ToString() : "?") + detail;
            }
            catch (Exception e)
            {
                return "? (" + e.Message + ")";
            }
        }

        private static void WriteTree(StringBuilder text, VisualElement element, int depth, ref int lines)
        {
            if (lines >= MaxLayoutLines || element == null) return;
            if (element.name != null && element.name.StartsWith("ShipCustomizer")) return;
            Rect bounds = element.worldBound;
            string classes = string.Join(" ", element.GetClasses().ToArray());
            text.AppendLine(string.Format("{0}{1} \"{2}\" [{3}] x{4:0} y{5:0} w{6:0} h{7:0}{8}", new string(' ', 2 + depth * 2), element.GetType().Name, element.name, classes,
                bounds.x, bounds.y, bounds.width, bounds.height, element.resolvedStyle.display == DisplayStyle.None ? " hidden" : ""));
            lines++;
            if (depth >= 12) return;
            foreach (VisualElement child in element.Children()) WriteTree(text, child, depth + 1, ref lines);
        }
    }
}
