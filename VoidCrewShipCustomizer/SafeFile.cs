using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Reading and writing the mod's JSON files so they can't be lost: a write goes to a temporary file first, on the disk,
    /// and then replaces the old one in one step (a crash or power cut leaves either the old or the new file, never neither,
    /// and the one before is kept as ".bak"). A file that can't be read right now (held open by a virus scanner or a backup)
    /// is not overwritten with an empty one, and a damaged one is kept as ".broken" while its ".bak" is used instead.
    /// </summary>
    internal static class SafeFile
    {
        public enum ReadResult
        {
            /// <summary>Read; or there is no file yet (a first start).</summary>
            Ok,
            /// <summary>The file is there but can't be read now: the caller must not save over it this session.</summary>
            Unreadable
        }

        /// <summary>Files found damaged when read: their next save must not keep the damaged one as ".bak" (the last good copy).</summary>
        private static readonly HashSet<string> Damaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reads a JSON file: the file itself, or when it can't be understood (empty, cut short or damaged) the copy kept
        /// before the last save (".bak"), then a save that was cut short (".tmp"). Null when there is none (a first start)
        /// or none can be understood (then it starts fresh; the damaged file is kept as ".broken").
        /// </summary>
        public static ReadResult Load<T>(string path, string what, out T value) where T : class
        {
            value = null;
            string text;
            if (Read(path, out text) == ReadResult.Unreadable) return ReadResult.Unreadable;
            if (text == null || TryParse(text, out value)) return ReadResult.Ok;

            Damaged.Add(path);
            KeepBroken(path);
            Log.Error("Could not understand " + path + " (" + what + "); it is kept as " + Path.GetFileName(path) + ".broken.");
            foreach (string copy in new[] { path + ".bak", path + ".tmp" })
            {
                string copyText;
                if (!TryReadText(copy, out copyText) || !TryParse(copyText, out value)) continue;
                Log.Warn("Brought back " + what + " from " + Path.GetFileName(copy) + ", the copy from before the last save.");
                return ReadResult.Ok;
            }
            Log.Error("No earlier copy of " + what + " could be read either, so it starts fresh.");
            return ReadResult.Ok;
        }

        private static bool TryParse<T>(string text, out T value) where T : class
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                value = JsonConvert.DeserializeObject<T>(text);
            }
            catch (Exception e)
            {
                Log.Verbose("Could not read JSON: " + e.Message);
            }
            return value != null;
        }

        private static bool TryReadText(string path, out string text)
        {
            text = null;
            try
            {
                if (File.Exists(path)) text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Verbose("Could not read " + path + ": " + e.Message);
            }
            return text != null;
        }

        /// <summary>The file's text, or null when there is none. A finished write that a crash left as ".tmp" is used too.</summary>
        public static ReadResult Read(string path, out string text)
        {
            text = null;
            string temp = path + ".tmp";
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        text = File.ReadAllText(path);
                        return ReadResult.Ok;
                    }
                    if (File.Exists(temp))
                    {
                        // An older version deleted the old file before moving the new one in; a crash in between left this.
                        text = File.ReadAllText(temp);
                        Log.Warn("Found " + temp + " without " + Path.GetFileName(path) + " (a save that was cut short) and used it.");
                        return ReadResult.Ok;
                    }
                    return ReadResult.Ok;
                }
                catch (IOException e)
                {
                    if (attempt >= 4)
                    {
                        Log.Error("Could not read " + path + " (" + e.Message + "). It is left as it is: changes made this session won't be saved to it.");
                        return ReadResult.Unreadable;
                    }
                    Thread.Sleep(100);
                }
                catch (UnauthorizedAccessException e)
                {
                    Log.Error("Could not read " + path + " (" + e.Message + "). It is left as it is: changes made this session won't be saved to it.");
                    return ReadResult.Unreadable;
                }
            }
        }

        /// <summary>
        /// Writes the file in one step: the new text goes to ".tmp", on the disk, then replaces the file, whose old version is
        /// kept as ".bak" until the next save (unless it was damaged: then the ".bak" from before stays).
        /// </summary>
        public static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                // Really on the disk before it replaces the old file, so a power cut can't leave an empty file behind.
                stream.Flush(true);
            }
            if (!File.Exists(path))
            {
                File.Move(temp, path);
                Damaged.Remove(path);
            }
            else if (Damaged.Contains(path))
            {
                File.Replace(temp, path, null, true);
                Damaged.Remove(path);
            }
            else
            {
                File.Replace(temp, path, path + ".bak", true);
            }
        }

        /// <summary>
        /// Keeps a copy of a file that could not be understood, before it is replaced: the file itself, or the save that was
        /// cut short (".tmp") when only that is there (it is what was read).
        /// </summary>
        public static void KeepBroken(string path)
        {
            try
            {
                string read = File.Exists(path) ? path : (File.Exists(path + ".tmp") ? path + ".tmp" : null);
                if (read != null) File.Copy(read, path + ".broken", true);
            }
            catch
            {
                // nothing more to do
            }
        }
    }
}
