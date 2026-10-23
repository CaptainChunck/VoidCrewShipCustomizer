using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// A CUSTOM loadout as one line of text to share (in a chat, on a forum): "SC1-" and the loadout, compressed and in
    /// web-safe base64, so it survives being pasted anywhere. Bringing one in checks it like a change from another player:
    /// only what the ship has, what fits, and what this PC's settings allow.
    /// </summary>
    internal static class ShareCodes
    {
        /// <summary>A compressed code; "SC0-" is the same uncompressed, made when compression isn't available.</summary>
        public const string Prefix = "SC1-";
        public const string PlainPrefix = "SC0-";

        /// <summary>The longest code read (a whole loadout compresses to well under 2000 characters).</summary>
        private const int MaxCodeLength = 40000;
        private const int MaxJsonLength = 200000;

        private static readonly Regex Code = new Regex(@"SC([01])-([A-Za-z0-9_-]+)");
        private static readonly Regex HullHex = new Regex("^[0-9A-Fa-f]{32}$");

        private sealed class Shared
        {
            public int V = 1;
            /// <summary>The ship type (its GUID in hex): a code is for one ship type.</summary>
            public string Hull;
            public HullConfig Config;
        }

        /// <summary>The share code of a CUSTOM loadout.</summary>
        public static string Make(string hullHex, HullConfig config)
        {
            string json = JsonConvert.SerializeObject(new Shared { Hull = hullHex, Config = config }, Formatting.None);
            byte[] raw = Encoding.UTF8.GetBytes(json);
            try
            {
                return Prefix + ToBase64Url(Deflate(raw));
            }
            catch (Exception e)
            {
                Log.Verbose("Could not compress the share code, so it is longer: " + e.Message);
                return PlainPrefix + ToBase64Url(raw);
            }
        }

        /// <summary>
        /// Reads a share code out of pasted text (other words around it are fine). False, with the reason in a few words,
        /// when the text holds none or it can't be read.
        /// </summary>
        public static bool TryRead(string text, out string hullHex, out HullConfig config, out string why)
        {
            hullHex = null;
            config = null;
            why = null;
            Match match = Code.Match(text ?? "");
            if (!match.Success)
            {
                why = "there is no share code on the clipboard (copy one first: they start with " + Prefix + ")";
                return false;
            }
            if (match.Groups[2].Value.Length > MaxCodeLength)
            {
                why = "the share code is too long to be a loadout";
                return false;
            }
            try
            {
                byte[] bytes = FromBase64Url(match.Groups[2].Value);
                if (match.Groups[1].Value == "1") bytes = Inflate(bytes);
                var shared = JsonConvert.DeserializeObject<Shared>(Encoding.UTF8.GetString(bytes));
                if (shared == null || shared.Config == null || string.IsNullOrEmpty(shared.Hull))
                {
                    why = "the share code is incomplete";
                    return false;
                }
                // A ship type's GUID: 32 hex digits (a hand-made code could hold anything).
                if (!HullHex.IsMatch(shared.Hull))
                {
                    why = "the share code is damaged (was all of it copied?)";
                    return false;
                }
                hullHex = shared.Hull.ToLowerInvariant();
                config = shared.Config.Normalized();
                return true;
            }
            catch (Exception e)
            {
                Log.Verbose("Could not read a share code: " + e.Message);
                why = "the share code is damaged (was all of it copied?)";
                return false;
            }
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress)) deflate.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        /// <summary>Uncompresses, stopping at a size no loadout reaches (a made-up code can't fill the memory).</summary>
        private static byte[] Inflate(byte[] packed)
        {
            using (var input = new MemoryStream(packed))
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = inflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > MaxJsonLength) throw new InvalidDataException("too big for a loadout");
                }
                return output.ToArray();
            }
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static byte[] FromBase64Url(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
