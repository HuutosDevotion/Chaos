using System.Text.RegularExpressions;

namespace Chaos.Client;

// Regex patterns and cursor-position helpers used by MainWindow to detect which
// inline formatting spans enclose the current cursor position.  Extracted to an
// internal class so the unit-test project can exercise them directly.
internal static class FormatDetection
{
    internal static readonly Regex BoldItalicSpan =
        new(@"\*\*\*(?!\s)(.*?)(?<!\s)\*\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    internal static readonly Regex BoldSpan =
        new(@"\*\*(?!\s)(.*?)(?<!\s)\*\*",     RegexOptions.Compiled | RegexOptions.Singleline);
    internal static readonly Regex UnderlineSpan =
        new(@"__(.*?)__",                       RegexOptions.Compiled | RegexOptions.Singleline);
    internal static readonly Regex StrikeSpan =
        new(@"~~(.*?)~~",                       RegexOptions.Compiled | RegexOptions.Singleline);
    internal static readonly Regex CodeSpan =
        new(@"`(.+?)`",                         RegexOptions.Compiled | RegexOptions.Singleline);
    internal static readonly Regex TripleInlineCodeSpan =
        new(@"```(.+?)```",                     RegexOptions.Compiled | RegexOptions.Singleline);

    internal static readonly Regex AlignmentLine =
        new(@"^:(left|center|right|justify) (.*):$", RegexOptions.Compiled);
    // Multiline version: ^ anchors per-line so it can find blocks anywhere in the full text.
    internal static readonly Regex AlignmentBlock =
        new(@"^:(left|center|right|justify) ([\s\S]+?):$", RegexOptions.Compiled | RegexOptions.Multiline);

    // Returns true when `pos` falls inside the content region of any span matched by `pattern`.
    // markerLen is the length of the opening/closing delimiter (** = 2, __ = 2, ~~ = 2).
    internal static bool IsCursorInSpan(string text, int pos, Regex pattern, int markerLen)
    {
        foreach (Match m in pattern.Matches(text))
            if (pos >= m.Index + markerLen && pos <= m.Index + m.Length - markerLen)
                return true;
        return false;
    }

    // Masks only the ** / *** delimiter characters (not the span content) so that
    // lone * inside a bold span can still be found as italic markers.
    internal static bool[] MaskBoldDelimiters(string text)
    {
        var masked = new bool[text.Length];
        foreach (Match m in BoldItalicSpan.Matches(text))
        {
            for (int j = m.Index;                j < Math.Min(m.Index + 3,        text.Length); j++) masked[j] = true;
            for (int j = m.Index + m.Length - 3; j < Math.Min(m.Index + m.Length, text.Length); j++) masked[j] = true;
        }
        foreach (Match m in BoldSpan.Matches(text))
        {
            if (m.Length < 4) continue;
            masked[m.Index]                = true;
            masked[m.Index + 1]            = true;
            masked[m.Index + m.Length - 2] = true;
            masked[m.Index + m.Length - 1] = true;
        }
        return masked;
    }

    internal static bool IsCursorInItalicSpan(string text, int pos)
    {
        if (text.Length == 0) return false;
        bool[] masked = MaskBoldDelimiters(text);

        int openAt = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '*' || masked[i]) continue;
            if (openAt < 0)
                openAt = i;
            else
            {
                if (pos >= openAt + 1 && pos <= i) return true;
                openAt = -1;
            }
        }
        return false;
    }

    // Returns true when lineStartPos falls inside a ``` fenced block.
    // Counts ``` lines before the current line; odd = inside a block.
    internal static bool IsLineInFencedBlock(string text, int lineStartPos)
    {
        string prefix = text[..lineStartPos];
        int fenceCount = prefix.Replace("\r\n", "\n").Replace("\r", "\n")
                               .Split('\n')
                               .Count(l => l == "```" ||
                                           (l.StartsWith("```") && l[3..].Trim().All(char.IsDigit)
                                            && l.Length > 3));
        return fenceCount % 2 == 1;
    }

    // Returns the active alignment keyword ("left"/"center"/"right"/"justify") at `pos`,
    // or null if the cursor is not inside any alignment block.
    internal static string? GetActiveAlignment(string text, int pos)
    {
        int lineStartPos = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;
        int lineEndPos   = text.IndexOf('\n', lineStartPos);
        if (lineEndPos < 0) lineEndPos = text.Length;
        string currentLine = text[lineStartPos..lineEndPos];
        var alignMatch = AlignmentLine.Match(currentLine.TrimEnd('\r'));
        if (alignMatch.Success) return alignMatch.Groups[1].Value;
        foreach (Match m in AlignmentBlock.Matches(text))
            if (pos > m.Index && pos < m.Index + m.Length) return m.Groups[1].Value;
        return null;
    }
}
