using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VoidCrewShipCustomizer
{
    /// <summary>The game's texts with their rich-text tags (bold, colours): read as plain text, or in parts.</summary>
    internal static class RichText
    {
        private static readonly Regex Tag = new Regex("<[^>]*>");
        private static readonly Regex BlankLine = new Regex(@"\n\s*\n");

        /// <summary>A whole line in bold (maybe inside colour tags), as the game writes the headings in its descriptions.</summary>
        private static readonly Regex BoldLine = new Regex(@"^(<(?!/)[^>]+>)*<b>((?!</b>).)*</b>(</[^>]+>)*$", RegexOptions.IgnoreCase);

        /// <summary>Text without its tags: "&lt;b&gt;B.R.A.I.N.&lt;/b&gt;: Auto-Mechanic MkI" becomes "B.R.A.I.N.: Auto-Mechanic MkI".</summary>
        public static string Plain(string text)
        {
            return string.IsNullOrEmpty(text) ? "" : Tag.Replace(text, "").Trim();
        }

        /// <summary>
        /// The lines under the last heading of a description (a short line in bold on its own, such as a Blessed
        /// Homunculus's "Blessing"), keeping their tags, one line each. Without a heading, the last paragraph; null when
        /// there is only one paragraph, or nothing under the heading.
        /// </summary>
        public static string LastSection(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            string text = body.Replace("\r", "").Trim();
            string[] lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (IsHeading(lines[i])) return JoinLines(lines.Skip(i + 1));
            }
            string[] paragraphs = BlankLine.Split(text);
            if (paragraphs.Length < 2) return null;
            List<string> last = paragraphs[paragraphs.Length - 1].Split('\n').ToList();
            // A heading styled some other way: a short first line that isn't a sentence.
            string first = Plain(last[0]);
            if (last.Count > 1 && first.Length <= 24 && !first.EndsWith(".")) last.RemoveAt(0);
            return JoinLines(last);
        }

        private static bool IsHeading(string line)
        {
            string trimmed = line.Trim();
            int length = Plain(trimmed).Length;
            return length > 0 && length <= 40 && BoldLine.IsMatch(trimmed);
        }

        private static string JoinLines(IEnumerable<string> lines)
        {
            string[] kept = lines.Select(l => l.Trim()).Where(l => Plain(l).Length > 0).ToArray();
            return kept.Length > 0 ? string.Join("\n", kept) : null;
        }
    }
}
