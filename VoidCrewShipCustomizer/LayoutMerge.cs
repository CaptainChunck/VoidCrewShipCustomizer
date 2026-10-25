using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Several players changing the same CUSTOM loadout at once. A loadout is made of separate parts: each hardpoint (its
    /// overlapping positions together, with the module's contents), each storage rack slot, the homunculus, the starting
    /// alloys and the fabricator level. A change is merged part by part into the loadout as it is now, so changes to
    /// different parts never undo each other. When a player changes a part that someone else changed after the player
    /// last saw it, the first change stays and theirs is refused (and they are told), instead of one silently replacing
    /// the other.
    /// </summary>
    internal static class LayoutMerge
    {
        public const string Homunculus = "homunculus";
        public const string Alloys = "alloys";
        public const string Fabricator = "fabricator";
        private const string HardpointStart = "hp:";
        private const string RackStart = "rack:";

        /// <summary>The part a hardpoint position belongs to (all overlapping positions are one part).</summary>
        public static string HardpointPart(int group)
        {
            return HardpointStart + group.ToString(CultureInfo.InvariantCulture);
        }

        public static string RackPart(int rack, int slot)
        {
            return RackStart + rack.ToString(CultureInfo.InvariantCulture) + ":" + slot.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Whether a part is a hardpoint ("hp:" and its group), and which group.</summary>
        public static bool IsHardpoint(string part, out int group)
        {
            group = -1;
            return part != null && part.StartsWith(HardpointStart, StringComparison.Ordinal) &&
                   int.TryParse(part.Substring(HardpointStart.Length), NumberStyles.None, CultureInfo.InvariantCulture, out group);
        }

        /// <summary>Whether a part is a storage rack slot ("rack:", the rack and the slot), and which.</summary>
        public static bool IsRackSlot(string part, out int rack, out int slot)
        {
            rack = -1;
            slot = -1;
            if (part == null || !part.StartsWith(RackStart, StringComparison.Ordinal)) return false;
            string[] bits = part.Split(':');
            return bits.Length == 3 && int.TryParse(bits[1], NumberStyles.None, CultureInfo.InvariantCulture, out rack) &&
                   int.TryParse(bits[2], NumberStyles.None, CultureInfo.InvariantCulture, out slot);
        }

        /// <summary>Every part of a loadout for a ship with these hardpoint positions (by group) and racks (by slot count).</summary>
        public static List<string> Parts(IList<SocketInfo> sockets, IDictionary<int, int> rackSlots)
        {
            var parts = sockets.Select(s => HardpointPart(s.Group)).Distinct().ToList();
            foreach (KeyValuePair<int, int> rack in rackSlots.OrderBy(r => r.Key))
            {
                for (int i = 0; i < rack.Value; i++) parts.Add(RackPart(rack.Key, i));
            }
            parts.Add(Homunculus);
            parts.Add(Alloys);
            parts.Add(Fabricator);
            return parts;
        }

        /// <summary>A part's state as text, to compare two versions of a loadout.</summary>
        public static string StateOf(HullConfig config, IList<SocketInfo> sockets, string part)
        {
            if (config == null) return "";
            if (part == Homunculus) return config.Homunculus ?? "";
            if (part == Alloys) return config.Alloys.ToString(CultureInfo.InvariantCulture);
            if (part == Fabricator) return config.FabricatorLevel.ToString(CultureInfo.InvariantCulture);
            int group, rack, slot;
            if (IsHardpoint(part, out group)) return HardpointState(config, sockets, group);
            if (IsRackSlot(part, out rack, out slot))
            {
                List<string> codes;
                return config.Storage != null && config.Storage.TryGetValue(rack, out codes) && codes != null && slot < codes.Count ? codes[slot] ?? "" : "";
            }
            return "";
        }

        /// <summary>A hardpoint's state as text: what each of its positions holds, with the module's contents.</summary>
        private static string HardpointState(HullConfig config, IList<SocketInfo> sockets, int group)
        {
            var state = new List<string>();
            foreach (SocketInfo socket in sockets.Where(s => s.Group == group).OrderBy(s => s.Index))
            {
                string code;
                if (config.Slots == null || !config.Slots.TryGetValue(socket.Index, out code) || string.IsNullOrEmpty(code)) continue;
                List<string> contents;
                string inside = config.Contents != null && config.Contents.TryGetValue(socket.Index, out contents) && contents != null ? "[" + string.Join(",", contents.ToArray()) + "]" : "";
                state.Add(socket.Index + "=" + code + inside);
            }
            return string.Join(";", state.ToArray());
        }

        /// <summary>
        /// Whether the host's checks (<paramref name="apply"/>, the change after them) take out something a player chose for
        /// a part (<paramref name="wanted"/>, the change as sent): a module or weapon on one of the hardpoint's positions, an
        /// item for one of its module's slots, an item in a rack slot, or the homunculus. That happens when the host's
        /// settings are stricter than the player's (AllowAnySize, AnyItemOnAnyRack), or its game doesn't have the item. The
        /// numbers (alloys, fabricator level) are only kept in range, which takes nothing out.
        /// </summary>
        public static bool Dropped(HullConfig wanted, HullConfig apply, IList<SocketInfo> sockets, string part)
        {
            if (wanted == null || apply == null) return false;
            if (part == Homunculus) return !string.IsNullOrEmpty(wanted.Homunculus) && string.IsNullOrEmpty(apply.Homunculus);
            int group, rack, slot;
            if (IsHardpoint(part, out group))
            {
                foreach (SocketInfo socket in sockets)
                {
                    string code;
                    if (socket.Group != group || wanted.Slots == null || !wanted.Slots.TryGetValue(socket.Index, out code) || string.IsNullOrEmpty(code)) continue;
                    if (apply.Slots == null || !apply.Slots.TryGetValue(socket.Index, out code) || string.IsNullOrEmpty(code)) return true;
                    List<string> chosen, kept = null;
                    if (wanted.Contents == null || !wanted.Contents.TryGetValue(socket.Index, out chosen) || chosen == null) continue;
                    if (apply.Contents != null) apply.Contents.TryGetValue(socket.Index, out kept);
                    for (int i = 0; i < chosen.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(chosen[i]) && (kept == null || i >= kept.Count || string.IsNullOrEmpty(kept[i]))) return true;
                    }
                }
                return false;
            }
            if (IsRackSlot(part, out rack, out slot)) return StateOf(wanted, sockets, part).Length > 0 && StateOf(apply, sockets, part).Length == 0;
            return false;
        }

        /// <summary>The parts that differ between two versions of a loadout.</summary>
        public static List<string> Changed(HullConfig from, HullConfig to, IList<SocketInfo> sockets, IDictionary<int, int> rackSlots)
        {
            return Parts(sockets, rackSlots).Where(p => StateOf(from, sockets, p) != StateOf(to, sockets, p)).ToList();
        }

        /// <summary>
        /// A player's change merged into the loadout as it is now.
        /// - <paramref name="seen"/> is the loadout as the player last saw it from the host, <paramref name="wanted"/> the
        ///   same with their change, both as they were sent: which parts the player changed, and which changed since they
        ///   looked, are told from those (the host's own loadout as it was shared, so whatever the host's checks would take
        ///   out of it never reads as a change).
        /// - <paramref name="apply"/> is <paramref name="wanted"/> after the host's checks: a changed part takes its state
        ///   from it. A part whose choice those checks take out (<see cref="Dropped"/>) is refused instead (0.9.5), so a pick
        ///   the host's settings don't allow never empties that slot or hardpoint for everyone.
        /// - A part that changed since the player looked is refused and keeps the other change, unless
        ///   <paramref name="ownChange"/> says that latest change was this same player's (an earlier change of theirs that
        ///   hasn't come back to them yet).
        /// Parts the player didn't change keep their current state (but see <see cref="TakeBack"/>). CopiedFrom (a note only)
        /// follows the player's change.
        /// </summary>
        public static HullConfig Merge(HullConfig current, HullConfig seen, HullConfig wanted, HullConfig apply, IList<SocketInfo> sockets, IDictionary<int, int> rackSlots,
                                       Func<string, bool> ownChange, List<string> applied, List<string> refused)
        {
            HullConfig result = current.Clone().Normalized();
            foreach (string part in Parts(sockets, rackSlots))
            {
                string before = StateOf(seen, sockets, part);
                if (before == StateOf(wanted, sockets, part)) continue;
                if (Dropped(wanted, apply, sockets, part))
                {
                    refused.Add(part);
                    continue;
                }
                string after = StateOf(apply, sockets, part);
                string now = StateOf(current, sockets, part);
                if (now == after) continue;
                if (now != before && (ownChange == null || !ownChange(part)))
                {
                    refused.Add(part);
                    continue;
                }
                Copy(apply, result, sockets, part);
                applied.Add(part);
            }
            if ((seen.CopiedFrom ?? "") != (wanted.CopiedFrom ?? "")) result.CopiedFrom = apply.CopiedFrom ?? "";
            return result;
        }

        /// <summary>
        /// After <see cref="Merge"/> (same arguments, and its <paramref name="result"/>): a part the player put back as it was,
        /// while their own change to it was still on its way back to them (A to B, then back to A before B came back). The
        /// change they sent next reads as leaving that part alone, since they never saw B from the host, but their terminal
        /// shows A again and A is what they want. Only when <paramref name="ownChangeFromSameView"/> says the part's latest
        /// change is theirs, made from the very loadout of the host's this change was made from: if another one reached them
        /// in between, their terminal showed that one for a moment instead of their change, and they may never have meant to
        /// take it back, so the part is left alone. A part whose old state the host's checks would take out is refused.
        /// </summary>
        public static void TakeBack(HullConfig current, HullConfig seen, HullConfig wanted, HullConfig apply, IList<SocketInfo> sockets, IDictionary<int, int> rackSlots,
                                    Func<string, bool> ownChangeFromSameView, HullConfig result, List<string> applied, List<string> refused)
        {
            if (ownChangeFromSameView == null) return;
            foreach (string part in Parts(sockets, rackSlots))
            {
                string want = StateOf(wanted, sockets, part);
                string now = StateOf(current, sockets, part);
                if (StateOf(seen, sockets, part) != want || now == want || now == StateOf(apply, sockets, part) || !ownChangeFromSameView(part)) continue;
                if (Dropped(wanted, apply, sockets, part))
                {
                    refused.Add(part);
                    continue;
                }
                Copy(apply, result, sockets, part);
                applied.Add(part);
            }
        }

        /// <summary>Gives <paramref name="to"/> the state <paramref name="from"/> has for one part.</summary>
        public static void Copy(HullConfig from, HullConfig to, IList<SocketInfo> sockets, string part)
        {
            if (part == Homunculus)
            {
                to.Homunculus = from.Homunculus ?? "";
                return;
            }
            if (part == Alloys)
            {
                to.Alloys = from.Alloys;
                return;
            }
            if (part == Fabricator)
            {
                to.FabricatorLevel = from.FabricatorLevel;
                return;
            }
            int group, rack, slot;
            if (IsHardpoint(part, out group))
            {
                foreach (SocketInfo socket in sockets)
                {
                    if (socket.Group != group) continue;
                    to.Slots.Remove(socket.Index);
                    to.Contents.Remove(socket.Index);
                    string code;
                    if (from.Slots != null && from.Slots.TryGetValue(socket.Index, out code) && !string.IsNullOrEmpty(code)) to.Slots[socket.Index] = code;
                    List<string> contents;
                    if (from.Contents != null && from.Contents.TryGetValue(socket.Index, out contents) && contents != null) to.Contents[socket.Index] = contents.ToList();
                }
                return;
            }
            if (IsRackSlot(part, out rack, out slot))
            {
                List<string> codes;
                if (!to.Storage.TryGetValue(rack, out codes) || codes == null)
                {
                    codes = new List<string>();
                    to.Storage[rack] = codes;
                }
                while (codes.Count <= slot) codes.Add("");
                codes[slot] = StateOf(from, sockets, part);
            }
        }
    }
}
