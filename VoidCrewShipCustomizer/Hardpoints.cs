using System.Collections.Generic;
using System.Linq;
using CG.Ship.Hull;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The rules for hardpoints, kept free of game objects so they can be tested offline. A ship's build sockets can
    /// overlap: a 2x1 (medium) hardpoint is one 2x1 socket plus the two 1x1 sockets inside it, and only one of two
    /// overlapping sockets can hold a module at a time, as in the game (BuildSocket.AnyBlockingNeighbors).
    /// </summary>
    internal static class Hardpoints
    {
        public const float CellSize = 4f;

        /// <summary>Centres of the grid cells a socket covers; the same layout as BuildSocket.LinkBuildSocketNeighbors.</summary>
        public static Vector3[] CellsFor(BuildSize size, Vector3 position, Vector3 right, Vector3 forward)
        {
            int width, height, depth;
            size.GetDimensions(out width, out height, out depth);
            float offsetX = (float)(width / 2) - 0.5f * (float)(1 - width % 2);
            float offsetZ = (float)(depth / 2) - 0.5f * (float)(1 - depth % 2);
            var cells = new Vector3[width * depth];
            int k = 0;
            for (int i = 0; i < depth; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    cells[k++] = position + right * ((j - offsetX) * CellSize) + forward * ((i - offsetZ) * CellSize);
                }
            }
            return cells;
        }

        /// <summary>Links sockets whose cells overlap and numbers the hardpoints (groups of overlapping sockets).</summary>
        public static int LinkOverlaps(List<SocketInfo> sockets)
        {
            foreach (SocketInfo socket in sockets)
            {
                socket.Overlaps.Clear();
                socket.Group = -1;
            }
            for (int a = 0; a < sockets.Count; a++)
            {
                for (int b = a + 1; b < sockets.Count; b++)
                {
                    if (!CellsOverlap(sockets[a], sockets[b])) continue;
                    sockets[a].Overlaps.Add(b);
                    sockets[b].Overlaps.Add(a);
                }
            }

            int group = 0;
            foreach (SocketInfo start in sockets)
            {
                if (start.Group >= 0) continue;
                var stack = new Stack<SocketInfo>();
                start.Group = group;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    foreach (int next in stack.Pop().Overlaps)
                    {
                        if (sockets[next].Group >= 0) continue;
                        sockets[next].Group = group;
                        stack.Push(sockets[next]);
                    }
                }
                group++;
            }
            return group;
        }

        private static bool CellsOverlap(SocketInfo a, SocketInfo b)
        {
            foreach (Vector3 ca in a.Cells)
            {
                foreach (Vector3 cb in b.Cells)
                {
                    // The game links neighbours closer than 2 m (sqrMagnitude < 4); cells of different hardpoints are 4 m apart.
                    if ((ca - cb).sqrMagnitude < CellSize) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The empty positions to show a "+" on: free space only, biggest first, never two that overlap. An empty 2x1
        /// hardpoint shows one "+" rather than itself plus the two 1x1 positions inside it.
        /// </summary>
        public static HashSet<int> OfferedEmpty(List<SocketInfo> sockets, ICollection<int> fitted, int drawnCount)
        {
            var offered = new HashSet<int>();
            foreach (SocketInfo socket in sockets.OrderByDescending(s => (int)s.Size).ThenBy(s => s.Index))
            {
                if (socket.Index >= drawnCount || fitted.Contains(socket.Index)) continue;
                if (socket.Overlaps.Any(fitted.Contains) || socket.Overlaps.Any(offered.Contains)) continue;
                offered.Add(socket.Index);
            }
            return offered;
        }

        /// <summary>
        /// Where a module of the given size goes when it is picked from the clicked position: the position itself if the
        /// size matches, otherwise the overlapping position of that size with the fewest modules in the way. Null when
        /// nothing of that size overlaps the clicked position.
        /// </summary>
        public static SocketInfo TargetFor(BuildSize size, SocketInfo clicked, List<SocketInfo> sockets, ICollection<int> fitted, int drawnCount)
        {
            if (clicked.Size == size) return clicked;
            return clicked.Overlaps
                .Select(i => sockets[i])
                .Where(s => s.Size == size && s.Index < drawnCount)
                .OrderBy(s => fitted.Count(k => k == s.Index || s.Overlaps.Contains(k)))
                .ThenBy(s => s.Index)
                .FirstOrDefault();
        }

        /// <summary>The positions whose modules must come out for a module to go in the target position.</summary>
        public static List<int> Displaced(SocketInfo target, ICollection<int> fitted)
        {
            return fitted.Where(k => k == target.Index || target.Overlaps.Contains(k)).ToList();
        }
    }
}
