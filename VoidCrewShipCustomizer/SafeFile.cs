using System;
using System.IO;
using System.Threading;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Reading and writing the mod's JSON files so they can't be lost: a write goes to a temporary file first and then
    /// replaces the old one in one step (a crash leaves either the old or the new file, never neither), and a file that
    /// can't be read right now (held open by a virus scanner or a backup) is not overwritten with an empty one.
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

        /// <summary>Writes the file in one step: the old version is kept as ".bak" until the next save.</summary>
        public static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
            else File.Move(temp, path);
        }

        /// <summary>Keeps a copy of a file that could not be understood, before it is replaced.</summary>
        public static void KeepBroken(string path)
        {
            try
            {
                if (File.Exists(path)) File.Copy(path, path + ".broken", true);
            }
            catch
            {
                // nothing more to do
            }
        }
    }
}
