using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chaos.Client;

/// <summary>
/// Applies live formatting preview to a RichTextBox FlowDocument.
/// Uses a stack-based parser so nested formats like ~~**__*text*__**~~ work correctly.
/// Pre-splits Runs at all style boundaries, then assigns styles directly to each Run
/// to avoid stale-pointer issues from TextRange splitting.
/// </summary>
internal sealed class InlineFormatPreview
{
    private static readonly FontFamily ConsolasFont = new("Consolas");

    private readonly Brush _defaultForeground;
    private readonly Brush _mutedBrush;

    public InlineFormatPreview(Brush defaultForeground, Brush mutedBrush)
    {
        _defaultForeground = defaultForeground;
        _mutedBrush = mutedBrush;
    }

    public void Apply(RichTextBox rtb)
    {
        // Save caret as a character offset within its paragraph.
        // GetOffsetToPosition counts structural symbols which change when Runs split,
        // so we count actual text characters instead.
        var caretPara = rtb.CaretPosition.Paragraph;
        int caretCharOffset = caretPara != null
            ? new TextRange(caretPara.ContentStart, rtb.CaretPosition).Text.Length
            : -1;

        foreach (var para in rtb.Document.Blocks.OfType<Paragraph>().ToList())
            ApplyToParagraph(para);

        // Restore caret to the same character offset.
        if (caretPara != null && caretCharOffset >= 0)
            rtb.CaretPosition = GetPositionAtCharOffset(caretPara, caretCharOffset);
    }

    // ── Types ───────────────────────────────────────────────────────────────

    private enum Fmt { Bold, Italic, Underline, Strike, Code }

    private record struct Token(int Pos, int Len, Fmt Type);

    private record struct FmtSpan(int OpenPos, int OpenLen, int ClosePos, int CloseLen, Fmt Type)
    {
        public int ContentStart => OpenPos + OpenLen;
        public int ContentEnd => ClosePos;
    }

    private record struct RunInfo(Run Run, int SegmentOffset);

    private record struct TextSegment(string Text, List<RunInfo> Runs);

    // ── Core ────────────────────────────────────────────────────────────────

    private void ApplyToParagraph(Paragraph para)
    {
        // Reset all Run formatting to defaults.
        foreach (var run in para.Inlines.OfType<Run>())
        {
            run.FontWeight = FontWeights.Normal;
            run.FontStyle = FontStyles.Normal;
            run.TextDecorations = null;
            run.Foreground = _defaultForeground;
            run.FontFamily = para.FontFamily;
        }

        var segments = BuildTextSegments(para);

        for (int si = 0; si < segments.Count; si++)
        {
            var seg = segments[si];
            if (string.IsNullOrEmpty(seg.Text)) continue;

            var spans = Parse(seg.Text);
            if (spans.Count == 0) continue;

            // Collect all boundary positions where styles change.
            var boundaries = new SortedSet<int>();
            foreach (var s in spans)
            {
                boundaries.Add(s.OpenPos);
                boundaries.Add(s.ContentStart);
                boundaries.Add(s.ContentEnd);
                boundaries.Add(s.ClosePos);
                boundaries.Add(s.ClosePos + s.CloseLen);
            }

            // Split Runs at boundaries so each Run falls entirely
            // within one style region.
            SplitRunsAtBoundaries(para, seg, boundaries);

            // Rebuild segment with the now-split Runs.
            var freshSegments = BuildTextSegments(para);
            if (si >= freshSegments.Count) continue;
            var freshSeg = freshSegments[si];

            // Assign styles directly to each Run.
            foreach (var ri in freshSeg.Runs)
            {
                int runStart = ri.SegmentOffset;
                int runEnd = runStart + ri.Run.Text.Length;
                if (runEnd <= runStart) continue;

                bool muted = false;
                bool bold = false, italic = false, strike = false, underline = false, code = false;

                foreach (var s in spans)
                {
                    bool isOpenDelim = runStart >= s.OpenPos && runEnd <= s.ContentStart;
                    bool isCloseDelim = runStart >= s.ClosePos && runEnd <= s.ClosePos + s.CloseLen;
                    if (isOpenDelim || isCloseDelim)
                        muted = true;

                    if (runStart >= s.ContentStart && runEnd <= s.ContentEnd)
                    {
                        switch (s.Type)
                        {
                            case Fmt.Bold: bold = true; break;
                            case Fmt.Italic: italic = true; break;
                            case Fmt.Strike: strike = true; break;
                            case Fmt.Underline: underline = true; break;
                            case Fmt.Code: code = true; break;
                        }
                    }
                }

                if (muted)
                {
                    ri.Run.Foreground = _mutedBrush;
                    // Delimiters only get dimmed — no content styles.
                    continue;
                }

                if (bold) ri.Run.FontWeight = FontWeights.Bold;
                if (italic) ri.Run.FontStyle = FontStyles.Italic;
                if (code) ri.Run.FontFamily = ConsolasFont;

                if (strike || underline)
                {
                    var decos = new TextDecorationCollection();
                    if (strike) foreach (var d in TextDecorations.Strikethrough) decos.Add(d);
                    if (underline) foreach (var d in TextDecorations.Underline) decos.Add(d);
                    ri.Run.TextDecorations = decos;
                }
            }
        }
    }

    /// <summary>
    /// Splits Runs in the paragraph at the given text-offset boundaries (relative to the segment).
    /// Builds all pieces first, then replaces the original Run in one swap to avoid caret movement.
    /// </summary>
    private static void SplitRunsAtBoundaries(Paragraph para, TextSegment seg, SortedSet<int> boundaries)
    {
        foreach (var ri in seg.Runs.ToList())
        {
            var run = ri.Run;
            int runStart = ri.SegmentOffset;
            int runEnd = runStart + run.Text.Length;

            var splitPoints = boundaries
                .Where(b => b > runStart && b < runEnd)
                .Select(b => b - runStart)
                .OrderBy(b => b)
                .ToList();

            if (splitPoints.Count == 0) continue;

            // Build all pieces without mutating the original Run.
            var pieces = new List<string>();
            int prev = 0;
            foreach (int sp in splitPoints)
            {
                pieces.Add(run.Text[prev..sp]);
                prev = sp;
            }
            pieces.Add(run.Text[prev..]);

            // Insert pieces after the original, then remove the original.
            var anchor = run;
            foreach (string piece in pieces)
            {
                var newRun = new Run(piece)
                {
                    Foreground = run.Foreground,
                    FontWeight = run.FontWeight,
                    FontStyle = run.FontStyle,
                    FontFamily = run.FontFamily,
                };
                para.Inlines.InsertAfter(anchor, newRun);
                anchor = newRun;
            }
            para.Inlines.Remove(run);
        }
    }

    // ── Stack-based parser ──────────────────────────────────────────────────

    private static List<FmtSpan> Parse(string text)
    {
        var spans = new List<FmtSpan>();
        var codeMask = new bool[text.Length];

        FindCodeSpans(text, spans, codeMask);

        var stack = new List<Token>();
        int i = 0;

        while (i < text.Length)
        {
            if (codeMask[i]) { i++; continue; }

            if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
            {
                MatchOrPush(stack, spans, Fmt.Strike, i, 2);
                i += 2;
            }
            else if (i + 1 < text.Length && text[i] == '_' && text[i + 1] == '_')
            {
                MatchOrPush(stack, spans, Fmt.Underline, i, 2);
                i += 2;
            }
            else if (text[i] == '*')
            {
                int startPos = i;
                int count = 0;
                while (i < text.Length && text[i] == '*' && !codeMask[i]) { count++; i++; }
                ConsumeStars(stack, spans, startPos, count);
            }
            else
            {
                i++;
            }
        }

        return spans;
    }

    private static void MatchOrPush(List<Token> stack, List<FmtSpan> spans, Fmt type, int pos, int len)
    {
        if (stack.Count > 0 && stack[^1].Type == type)
        {
            var open = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            spans.Add(new FmtSpan(open.Pos, open.Len, pos, len, type));
        }
        else
        {
            stack.Add(new Token(pos, len, type));
        }
    }

    private static void ConsumeStars(List<Token> stack, List<FmtSpan> spans, int startPos, int count)
    {
        int pos = startPos;
        int remaining = count;

        while (remaining > 0)
        {
            if (remaining >= 2 && stack.Count > 0 && stack[^1].Type == Fmt.Bold)
            {
                var open = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                spans.Add(new FmtSpan(open.Pos, open.Len, pos, 2, Fmt.Bold));
                pos += 2;
                remaining -= 2;
            }
            else if (remaining >= 1 && stack.Count > 0 && stack[^1].Type == Fmt.Italic)
            {
                var open = stack[^1];
                stack.RemoveAt(stack.Count - 1);
                spans.Add(new FmtSpan(open.Pos, open.Len, pos, 1, Fmt.Italic));
                pos += 1;
                remaining -= 1;
            }
            else if (remaining >= 2)
            {
                stack.Add(new Token(pos, 2, Fmt.Bold));
                pos += 2;
                remaining -= 2;
            }
            else
            {
                stack.Add(new Token(pos, 1, Fmt.Italic));
                pos += 1;
                remaining -= 1;
            }
        }
    }

    private static void FindCodeSpans(string text, List<FmtSpan> spans, bool[] codeMask)
    {
        for (int i = 0; i <= text.Length - 3;)
        {
            if (text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`')
            {
                int close = text.IndexOf("```", i + 3, StringComparison.Ordinal);
                if (close >= 0)
                {
                    spans.Add(new FmtSpan(i, 3, close, 3, Fmt.Code));
                    MarkRange(codeMask, i, close + 3 - i);
                    i = close + 3;
                }
                else i++;
            }
            else i++;
        }

        for (int i = 0; i < text.Length;)
        {
            if (text[i] == '`' && !codeMask[i])
            {
                int close = i + 1;
                while (close < text.Length && (text[close] != '`' || codeMask[close]))
                    close++;
                if (close < text.Length)
                {
                    spans.Add(new FmtSpan(i, 1, close, 1, Fmt.Code));
                    MarkRange(codeMask, i, close + 1 - i);
                    i = close + 1;
                }
                else i++;
            }
            else i++;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void MarkRange(bool[] mask, int offset, int length)
    {
        for (int i = offset; i < offset + length && i < mask.Length; i++)
            mask[i] = true;
    }

    private static List<TextSegment> BuildTextSegments(Paragraph para)
    {
        var segments = new List<TextSegment>();
        var currentRuns = new List<RunInfo>();
        int offset = 0;

        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
            {
                currentRuns.Add(new RunInfo(run, offset));
                offset += run.Text.Length;
            }
            else
            {
                if (currentRuns.Count > 0)
                {
                    string text = string.Concat(currentRuns.Select(r => r.Run.Text));
                    segments.Add(new TextSegment(text, currentRuns));
                    currentRuns = new List<RunInfo>();
                    offset = 0;
                }
            }
        }

        if (currentRuns.Count > 0)
        {
            string text = string.Concat(currentRuns.Select(r => r.Run.Text));
            segments.Add(new TextSegment(text, currentRuns));
        }

        return segments;
    }

    /// <summary>
    /// Walks the paragraph's inlines counting text characters (and 1 per InlineUIContainer)
    /// to find the TextPointer at the given character offset.  This is stable across Run splits
    /// because it counts content chars, not structural symbols.
    /// </summary>
    private static TextPointer GetPositionAtCharOffset(Paragraph para, int charOffset)
    {
        int counted = 0;
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
            {
                int len = run.Text.Length;
                if (counted + len >= charOffset)
                    return run.ContentStart.GetPositionAtOffset(charOffset - counted) ?? para.ContentEnd;
                counted += len;
            }
            else if (inline is InlineUIContainer)
            {
                counted++;
                if (counted >= charOffset)
                    return inline.ElementEnd;
            }
        }
        return para.ContentEnd;
    }
}
