using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CG.Ship.Object;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// What a CUSTOM ship may start with in a Void Crew Fleet battle (PvP), so both teams start even:
    /// - Always: no relics, no Armory Exchange Key, no extra Fabricator modules, and none of the game's unfinished
    ///   "Placeholder" weapons (nor their crates, nor the Placeholder weapon mod). The menus leave these out, and Fleet's
    ///   SHIP RULES tab never lists them. Relics found during the match are the players' to keep.
    /// - The host's own rules from Fleet's lobby (Void Crew Fleet 0.3.0 on): which tiers, categories and items CUSTOM may
    ///   use, the fabricator's highest level and the most starting alloys. The menus grey out what they refuse.
    /// The host applies both to a copy of each CUSTOM loadout when the mission starts (the saved loadouts stay as they
    /// are) and tells every player with Ship Customizer what came off. Outside a Fleet Battle nothing here applies.
    /// </summary>
    internal static class MatchRules
    {
        public const string NotAllowedTag = "NOT ALLOWED IN THIS MATCH";

        /// <summary>
        /// The categories a match's rules allow or refuse, in display order: what FleetApi.IsCategoryAllowed is asked.
        /// There is no RELICS: relics are never allowed in a Fleet Battle.
        /// </summary>
        public static readonly string[] Categories =
        {
            "WEAPONS", "DEFENSE", "SUPPORT MODULES", "AMMO & POWER", "SUPPORT ITEMS", "MATERIALS", "ANIMUS CRATES",
            "WEAPON CRATES", "WEAPON MODS", "SPECIAL", "OTHER", "HOMUNCULUS"
        };

        // What a Fleet Battle never allows, found by id first (ids work in every language). The names and the fabricator's
        // module type below also catch new copies from game updates.
        private static readonly HashSet<string> PlaceholderGuids = new HashSet<string>
        {
            // The Placeholder weapons (their weapon crates have the same ids), and the Placeholder weapon mod.
            "224467f966b840d458b207ffe12a6f1f", "dd2b796a2324ca04bafb791c3f9f61e8", "179144e7c78b692498cb97f55905be21",
            "61e174ce25e53ab42aba29e58ea68d08", "867047444d0fe194d834e9660be9edf9", "180af8388bfe78641aac04e8bf9f2300",
            "66aa5b8e2f3f6c647af403f82cb184fc", "f9060adcc73928d4884829f7911ff094", "04d7e8570fa9a534c82af2fb507d2ab5"
        };
        private static readonly HashSet<string> FabricatorGuids = new HashSet<string> { "32fc0ec0c87a15a4d981170957fbcb9a", "e6922b2332bafa64a8872faa181d6fc6" };
        private const string ArmoryKeyGuid = "94d959e4b33888a4dafaa5f054d7c492";

        private static readonly Regex MkInName = new Regex(@"(?<![A-Za-z])Mk\s*(I{1,3})(?![A-Za-z])", RegexOptions.IgnoreCase);

        /// <summary>Host: what the rules took off this mission's CUSTOM ships, one line per ship.</summary>
        private static readonly List<string> MissionNotes = new List<string>();
        private static string[] _ruleOptions;

        /// <summary>True during a Void Crew Fleet battle (chosen in the hub, or running): the match rules apply.</summary>
        public static bool InMatch
        {
            get { return FleetLink.IsTeamMatch; }
        }

        // ---------------------------------------------------------------- what is allowed

        /// <summary>
        /// Why a Fleet Battle never allows this (a relic, a Placeholder weapon, an extra fabricator, the Armory Exchange
        /// Key), in a few words; null for everything else. The same whether or not a Fleet Battle is on.
        /// </summary>
        public static string AlwaysBanned(Option option)
        {
            if (option == null || string.IsNullOrEmpty(option.Code) || option.Code.Length < 3) return null;
            string guid = option.Code.Substring(2).ToLowerInvariant();
            string name = RichText.Plain(option.Name);
            if (option.RuleCategory == "RELICS") return "No relics in PvP.";
            if (PlaceholderGuids.Contains(guid) || name.StartsWith("Placeholder", StringComparison.OrdinalIgnoreCase)) return "Unfinished Placeholder: not in PvP.";
            if (option.Kind == OptionKind.Module && (FabricatorGuids.Contains(guid) || IsFabricator(option.ModuleType))) return "No extra fabricators in PvP.";
            if (option.Kind == OptionKind.Item && (guid == ArmoryKeyGuid || string.Equals(name, "Armory Exchange Key", StringComparison.OrdinalIgnoreCase))) return "No Armory Exchange Key in PvP.";
            return null;
        }

        /// <summary>The game's fabricator module type, by name (the offline checks can't load the game's module types).</summary>
        public const string FabricatorTypeName = "CG.Ship.Modules.FabricatorModule";

        private static bool IsFabricator(Type moduleType)
        {
            for (Type type = moduleType; type != null; type = type.BaseType)
            {
                if (type.FullName == FabricatorTypeName) return true;
            }
            return false;
        }

        /// <summary>Whether the menus leave this out: it is never allowed in a Fleet Battle, and one is on.</summary>
        public static bool Hidden(Option option)
        {
            return AlwaysBanned(option) != null && InMatch;
        }

        /// <summary>
        /// Why this match doesn't allow this on a CUSTOM ship, in a few words; null when it does (always outside a Fleet
        /// Battle). Besides what a Fleet Battle never allows, the host's rules can refuse its tier, its category or the
        /// thing itself. Banning a weapon bans its weapon crate too, so it can't be built from a crate instead.
        /// </summary>
        public static string WhyRefused(Option option)
        {
            if (option == null) return null;
            string banned = AlwaysBanned(option);
            if (banned != null) return InMatch ? banned : null;
            if (!FleetLink.ShipRulesActive) return null;
            int tier = TierOf(option);
            if (tier > 0 && !FleetLink.IsTierAllowed(tier)) return "Tier " + tier + " (" + TierName(tier) + ") isn't allowed in this match.";
            string category = RuleCategoryOf(option);
            if (!FleetLink.IsCategoryAllowed(category)) return category + " aren't allowed in this match.";
            bool itemBanned = !FleetLink.IsItemAllowed(option.Code) ||
                              (option.Kind == OptionKind.WeaponCrate && !FleetLink.IsItemAllowed("w:" + option.Guid.AsHex()));
            return itemBanned ? "Banned in this match." : null;
        }

        /// <summary>The category a match's rules see: a Blessed Homunculus is HOMUNCULUS in the cradle and on a rack alike.</summary>
        public static string RuleCategoryOf(Option option)
        {
            if (option.Kind == OptionKind.Item && Catalog.IsHomunculus(option.Code)) return "HOMUNCULUS";
            return string.IsNullOrEmpty(option.RuleCategory) ? "OTHER" : option.RuleCategory;
        }

        /// <summary>
        /// 1 to 3, the tier a match's rules allow or refuse: the MK I to MK III in the name (every weapon and module has
        /// one), otherwise the rarity (Common 1, Rare 2, Epic and Legendary 3: the strongest weapon mods are Legendary,
        /// so a "MK I only" match leaves them out too), otherwise 0 (Special, no rarity), which no tier rule refuses. A
        /// weapon crate has its weapon's name and rarity, so its weapon's tier.
        /// </summary>
        public static int TierOf(Option option)
        {
            Match mk = MkInName.Match(RichText.Plain(option.Name));
            if (mk.Success) return mk.Groups[1].Value.Length;
            switch (option.Rarity)
            {
                case RarityType.Common: return 1;
                case RarityType.Rare: return 2;
                case RarityType.Epic:
                case RarityType.Legendary: return 3;
                default: return 0;
            }
        }

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case 1: return "MK I, Common";
                case 2: return "MK II, Rare";
                case 3: return "MK III, Epic, Legendary";
                default: return "none";
            }
        }

        /// <summary>Whether the fabricator may start at this level (0 is MK I, which is always allowed: every run starts at it).</summary>
        public static bool FabricatorLevelAllowed(int level)
        {
            return level <= 0 || !FleetLink.ShipRulesActive || LevelAllowed(level, FleetLink.FabricatorMaxLevel, FleetLink.IsTierAllowed);
        }

        /// <summary>The highest level the fabricator may start at, up to this one.</summary>
        public static int AllowedFabricatorLevel(int level)
        {
            if (level <= 0 || !FleetLink.ShipRulesActive) return Math.Max(0, level);
            return HighestLevel(level, FleetLink.FabricatorMaxLevel, FleetLink.IsTierAllowed);
        }

        /// <summary>Fleet's rule: a level is allowed when its tier (level + 1) is, up to the highest fabricator level.</summary>
        internal static bool LevelAllowed(int level, int maxLevel, Func<int, bool> tierAllowed)
        {
            return level <= 0 || (level <= maxLevel && tierAllowed(level + 1));
        }

        internal static int HighestLevel(int level, int maxLevel, Func<int, bool> tierAllowed)
        {
            for (int candidate = level; candidate > 0; candidate--)
            {
                if (LevelAllowed(candidate, maxLevel, tierAllowed)) return candidate;
            }
            return 0;
        }

        /// <summary>Whether a run may start with this many alloys (-1, the game's own amount, always may).</summary>
        public static bool AlloysAllowed(int alloys)
        {
            return AllowedAlloys(alloys) == alloys;
        }

        /// <summary>The starting alloys within this match's limit (-1: the game's own amount).</summary>
        public static int AllowedAlloys(int alloys)
        {
            if (alloys < 0 || !FleetLink.ShipRulesActive) return alloys;
            return ClampAlloys(alloys, FleetLink.MaxStartAlloys);
        }

        /// <summary>Fleet's limit: -1 allows only the game's own amount (-1), int.MaxValue anything, otherwise up to it.</summary>
        internal static int ClampAlloys(int alloys, int max)
        {
            if (alloys < 0 || max < 0) return -1;
            return Math.Min(alloys, max);
        }

        // ---------------------------------------------------------------- a CUSTOM loadout against the rules

        /// <summary>Something a CUSTOM loadout has that this match doesn't allow.</summary>
        internal sealed class Issue
        {
            /// <summary>What it is ("Astral Gravity Well", "FABRICATOR MK III", "5000 ALLOYS").</summary>
            public string Name;
            /// <summary>Where it is ("HARDPOINT", "STORAGE RACK 1 · SLOT 3", "HOMUNCULUS", "FABRICATOR").</summary>
            public string Where;
            public string Why;
            /// <summary>The module, weapon or item; null for the fabricator's level and the alloys.</summary>
            public Option Option;
            /// <summary>For the fabricator's level and the alloys: what the ship starts with instead.</summary>
            public string Instead;
            /// <summary>Where it is, to go there: a hardpoint position, or a rack (core system) and slot; -1 when neither.</summary>
            public int Hardpoint = -1;
            public int Rack = -1;
            public int Slot = -1;
            /// <summary>The loadout part it is in (see LayoutMerge).</summary>
            public string Part;
        }

        /// <summary>
        /// What this CUSTOM loadout has that this match doesn't allow (nothing outside a Fleet Battle). With
        /// <paramref name="fix"/>, it is also taken off: hardpoints and rack slots are emptied, the homunculus becomes the
        /// standard one, and the fabricator's level and the alloys come down to what is allowed.
        /// </summary>
        public static List<Issue> Check(HullInfo hull, HullConfig config, bool fix)
        {
            var issues = new List<Issue>();
            if (hull == null || config == null || !InMatch) return issues;

            foreach (KeyValuePair<int, string> slot in config.Slots.ToList())
            {
                Option option = null;
                string why = Catalog.TryGetOption(slot.Value, out option) ? WhyRefused(option) : null;
                if (why == null) continue;
                issues.Add(new Issue { Name = RichText.Plain(option.Name), Where = HardpointName(hull, slot.Key), Why = why, Option = option, Hardpoint = slot.Key });
                if (fix)
                {
                    config.Slots.Remove(slot.Key);
                    config.Contents.Remove(slot.Key);
                }
            }

            // What the modules are loaded with, where the loadout chooses it (their usual contents keep to the rules by themselves).
            foreach (KeyValuePair<int, List<string>> inside in config.Contents.ToList())
            {
                if (inside.Value == null) continue;
                for (int i = 0; i < inside.Value.Count; i++)
                {
                    Option option = null;
                    string why = !string.IsNullOrEmpty(inside.Value[i]) && Catalog.TryGetOption(inside.Value[i], out option) ? WhyRefused(option) : null;
                    if (why == null) continue;
                    issues.Add(new Issue { Name = RichText.Plain(option.Name), Where = HardpointName(hull, inside.Key) + " · LOADED", Why = why, Option = option, Hardpoint = inside.Key });
                    if (fix) inside.Value[i] = "";
                }
            }

            foreach (ShelfInfo shelf in hull.Shelves)
            {
                List<string> codes;
                if (!config.Storage.TryGetValue(shelf.CoreIndex, out codes) || codes == null) continue;
                for (int i = 0; i < codes.Count; i++)
                {
                    Option option = null;
                    string why = !string.IsNullOrEmpty(codes[i]) && Catalog.TryGetOption(codes[i], out option) ? WhyRefused(option) : null;
                    if (why == null) continue;
                    issues.Add(new Issue { Name = RichText.Plain(option.Name), Where = shelf.Name + " · SLOT " + (i + 1), Why = why, Option = option, Rack = shelf.CoreIndex, Slot = i });
                    if (fix) codes[i] = "";
                }
            }

            Option homunculus = null;
            string homunculusWhy = !string.IsNullOrEmpty(config.Homunculus) && Catalog.TryGetOption(config.Homunculus, out homunculus) ? WhyRefused(homunculus) : null;
            if (homunculusWhy != null)
            {
                issues.Add(new Issue { Name = RichText.Plain(homunculus.Name), Where = "HOMUNCULUS", Why = homunculusWhy, Option = homunculus, Part = LayoutMerge.Homunculus });
                if (fix) config.Homunculus = "";
            }

            int level = AllowedFabricatorLevel(config.FabricatorLevel);
            if (level != config.FabricatorLevel)
            {
                issues.Add(new Issue
                {
                    Name = "FABRICATOR " + CustomLoadouts.FabricatorLevelName(config.FabricatorLevel),
                    Where = "FABRICATOR",
                    Why = "This match's fabricators go up to " + CustomLoadouts.FabricatorLevelName(level) + ".",
                    Part = LayoutMerge.Fabricator,
                    Instead = "its fabricator at " + CustomLoadouts.FabricatorLevelName(level) + " (not " + CustomLoadouts.FabricatorLevelName(config.FabricatorLevel) + ")"
                });
                if (fix) config.FabricatorLevel = level;
            }

            int alloys = AllowedAlloys(config.Alloys);
            if (alloys != config.Alloys)
            {
                issues.Add(new Issue
                {
                    Name = config.Alloys + " ALLOYS",
                    Where = "FABRICATOR",
                    Why = alloys < 0 ? "This match starts with the game's own alloys." : "This match allows up to " + alloys + " starting alloys.",
                    Part = LayoutMerge.Alloys,
                    Instead = (alloys < 0 ? "the game's own alloys" : alloys + " alloys") + " (not " + config.Alloys + ")"
                });
                if (fix) config.Alloys = alloys;
            }
            return issues;
        }

        /// <summary>"HARDPOINT 3": hardpoints are numbered by their group (overlapping positions are one hardpoint).</summary>
        private static string HardpointName(HullInfo hull, int position)
        {
            return position >= 0 && position < hull.Sockets.Count ? "HARDPOINT " + (hull.Sockets[position].Group + 1) : "HARDPOINT";
        }

        /// <summary>
        /// Host, at mission start: in a Fleet Battle, a copy of a CUSTOM loadout without what this match doesn't allow (the
        /// saved one stays as it is); every player with Ship Customizer is told what came off. Otherwise the loadout itself.
        /// </summary>
        public static HullConfig ForMission(HullInfo hull, HullConfig config, int ship)
        {
            if (!InMatch) return config;
            HullConfig copy = config.Clone().Normalized();
            List<Issue> removed = Check(hull, copy, true);
            if (removed.Count == 0) return copy;
            string note = Summary(ship, removed);
            Log.Info(note + " In detail: " + string.Join("; ", removed.Select(i => i.Name + " (" + i.Where + "): " + i.Why).ToArray()));
            MissionNotes.Add(note);
            RoomSync.PublishNotice(string.Join("\n", MissionNotes.ToArray()));
            return copy;
        }

        /// <summary>"Match rules: RED 1's CUSTOM loadout starts without Placeholder, 2x Astral Gravity Well; with ..."</summary>
        private static string Summary(int ship, List<Issue> removed)
        {
            string whose = FleetLink.IsTeamMatch || ship >= 2 ? FleetLink.ShipName(ship) + "'s CUSTOM loadout" : "the CUSTOM ship";
            var parts = new List<string>();
            List<string> things = removed.Where(i => i.Option != null).GroupBy(i => i.Name)
                .Select(g => (g.Count() > 1 ? g.Count() + "x " : "") + g.Key).ToList();
            if (things.Count > 8) things = things.Take(7).Concat(new[] { (things.Count - 7) + " more" }).ToList();
            if (things.Count > 0) parts.Add("without " + string.Join(", ", things.ToArray()));
            parts.AddRange(removed.Where(i => i.Instead != null).Select(i => "with " + i.Instead));
            return "Match rules: " + whose + " starts " + string.Join("; ", parts.ToArray()) + ".";
        }

        /// <summary>Back in the hub: the last mission's notes are done with.</summary>
        public static void OnHubLoaded()
        {
            MissionNotes.Clear();
            RoomSync.ClearNotice();
        }

        // ---------------------------------------------------------------- for Void Crew Fleet's SHIP RULES tab

        /// <summary>
        /// Everything a match's rules can allow or refuse, as "code|category|tier|name" (see
        /// ShipCustomizerApi.GetRuleOptions): the hardpoint options, the storage items and the Blessed Homunculi, each
        /// code once, by category in <see cref="Categories"/> order. Leaves out what a Fleet Battle never allows. Empty
        /// until the hub has loaded.
        /// </summary>
        public static string[] RuleOptions()
        {
            if (_ruleOptions != null) return (string[])_ruleOptions.Clone();
            if (!CustomizerRuntime.GameReady || !CustomLoadouts.Available) return new string[0];
            var seen = new HashSet<string>();
            var lines = new List<KeyValuePair<int, string>>();
            foreach (Option option in Catalog.SlotOptions.Concat(Catalog.ItemOptions).Concat(Catalog.HomunculusOptions))
            {
                if (option == null || AlwaysBanned(option) != null || !seen.Add(option.Code)) continue;
                string category = RuleCategoryOf(option);
                int order = Array.IndexOf(Categories, category);
                if (order < 0) continue;
                lines.Add(new KeyValuePair<int, string>(order, option.Code + "|" + category + "|" + TierOf(option) + "|" + RuleName(option, category)));
            }
            if (lines.Count == 0) return new string[0];
            // OrderBy keeps the catalog's order (rarity, then name) within a category.
            _ruleOptions = lines.OrderBy(l => l.Key).Select(l => l.Value).ToArray();
            Log.Info("Void Crew Fleet's match rules can allow or refuse " + _ruleOptions.Length + " CUSTOM options.");
            return (string[])_ruleOptions.Clone();
        }

        /// <summary>Plain text; the Blessed Homunculi all have the same name, so theirs adds the blessing.</summary>
        private static string RuleName(Option option, string category)
        {
            string name = RichText.Plain(option.Name);
            string blessing = category == "HOMUNCULUS" ? Catalog.BlessingOf(option) : null;
            if (blessing != null)
            {
                string plain = string.Join(" ", blessing.Split('\n').Select(RichText.Plain).ToArray());
                name += ": " + (plain.Length > 90 ? plain.Substring(0, 87) + "..." : plain);
            }
            return name.Replace("|", "/");
        }

        /// <summary>This match's rules for the MATCH RULES list: a name and a line each.</summary>
        public static List<KeyValuePair<string, string>> Describe()
        {
            var rules = new List<KeyValuePair<string, string>>
            {
                Rule("EVERY FLEET BATTLE", "No relics, Armory Exchange Key, extra Fabricator modules or Placeholder weapons on a CUSTOM ship. Relics found during the match are yours to use.")
            };
            if (!FleetLink.ShipRulesActive)
            {
                rules.Add(Rule("THE HOST'S RULES", "None set: everything else is allowed."));
                return rules;
            }
            int[] tiers = Enumerable.Range(1, 3).Where(FleetLink.IsTierAllowed).ToArray();
            rules.Add(Rule("TIERS", tiers.Length == 3 ? "All tiers." : tiers.Length == 0
                ? "No tiered weapons, modules or items."
                : "Only " + string.Join(" and ", tiers.Select(t => "tier " + t + " (" + TierName(t) + ")").ToArray()) + "."));
            string[] categories = Categories.Where(c => !FleetLink.IsCategoryAllowed(c)).ToArray();
            rules.Add(Rule("CATEGORIES", categories.Length == 0 ? "All allowed." : "Not allowed: " + string.Join(", ", categories) + "."));
            string[] banned = RuleOptions().Select(line => line.Split('|')).Where(parts => parts.Length >= 4 && !FleetLink.IsItemAllowed(parts[0]))
                .Select(parts => parts[3]).ToArray();
            rules.Add(Rule("BANNED", banned.Length == 0 ? "Nothing on its own." : banned.Length + ": " + string.Join(", ", banned.Take(8).ToArray()) + (banned.Length > 8 ? ", ..." : ".")));
            rules.Add(Rule("FABRICATOR", "Up to " + CustomLoadouts.FabricatorLevelName(AllowedFabricatorLevel(HullConfig.MaxFabricatorLevel)) + "."));
            int max = FleetLink.MaxStartAlloys;
            rules.Add(Rule("STARTING ALLOYS", max < 0 ? "The game's own amount only." : (max == int.MaxValue ? "No limit." : "Up to " + max + ".")));
            return rules;
        }

        private static KeyValuePair<string, string> Rule(string name, string detail)
        {
            return new KeyValuePair<string, string>(name, detail);
        }
    }
}
