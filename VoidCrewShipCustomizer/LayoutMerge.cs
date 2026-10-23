using System;
using System.Collections.Generic;
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

        /// <summary>The part a hardpoint position belongs to (all overlapping positions are one part).</summary>
        public static string HardpointPart(int group)
        {
            return "hp:" + group;
        }

        public static string RackPart(int rack, int slot)
        {
            return "rack:" + rack + ":" + slot;
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
            if (part == Alloys) return config.Alloys.ToString();
            if (part == Fabricator) return config.FabricatorLevel.ToString();
            if (part.StartsWith("hp:"))
            {
                int group = int.Parse(part.Substring(3));
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
            if (part.StartsWith("rack:"))
            {
                string[] bits = part.Split(':');
                int rack = int.Parse(bits[1]);
                int slot = int.Parse(bits[2]);
                List<string> codes;
                return config.Storage != null && config.Storage.TryGetValue(rack, out codes) && codes != null && slot < codes.Count ? codes[slot] ?? "" : "";
            }
            return "";
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
        ///   from it.
        /// - A part that changed since the player looked is refused and keeps the other change, unless
        ///   <paramref name="ownChange"/> says that latest change was this same player's (an earlier change of theirs that
        ///   hasn't come back to them yet).
        /// Parts the player didn't change keep their current state. CopiedFrom (a note only) follows the player's change.
        /// </summary>
        public static HullConfig Merge(HullConfig current, HullConfig seen, HullConfig wanted, HullConfig apply, IList<SocketInfo> sockets, IDictionary<int, int> rackSlots,
                                       Func<string, bool> ownChange, List<string> applied, List<string> refused)
        {
            HullConfig result = current.Clone().Normalized();
            foreach (string part in Parts(sockets, rackSlots))
            {
                string before = StateOf(seen, sockets, part);
                if (before == StateOf(wanted, sockets, part)) continue;
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
            if (part.StartsWith("hp:"))
            {
                int group = int.Parse(part.Substring(3));
                foreach (SocketInfo socket in sockets.Where(s => s.Group == group))
                {
                    to.Slots.Remove(socket.Index);
                    to.Contents.Remove(socket.Index);
                    string code;
                    if (from.Slots != null && from.Slots.TryGetValue(socket.Index, out code) && !string.IsNullOrEmpty(code)) to.Slots[socket.Index] = code;
                    List<string> contents;
                    if (from.Contents != null && from.Contents.TryGetValue(socket.Index, out contents) && contents != null) to.Contents[socket.Index] = contents.ToList();
                }
                return;
            }
            if (part.StartsWith("rack:"))
            {
                string[] bits = part.Split(':');
                int rack = int.Parse(bits[1]);
                int slot = int.Parse(bits[2]);
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
