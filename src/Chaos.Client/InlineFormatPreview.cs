using System.Text;
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
        // Anchor the caret to the Run it sits in before any structural
        // changes.  The Run object survives paragraph splits (moved, not
        // cloned) so we can restore the caret relative to it afterwards.
        Run? anchorRun = null;
        int anchorOffset = 0;
        var cPos = rtb.CaretPosition;
        foreach (var para in rtb.Document.Blocks.OfType<Paragraph>())
        {
            if (anchorRun != null) break;
            foreach (var inline in para.Inlines)
            {
                if (inline is Run run
                    && cPos.CompareTo(run.ContentStart) >= 0
                    && cPos.CompareTo(run.ContentEnd) <= 0)
                {
                    anchorRun = run;
                    anchorOffset = run.ContentStart.GetOffsetToPosition(cPos);
                    break;
                }
            }
        }

        // Split paragraphs that mix quoted and non-quoted lines so each
        // group gets its own paragraph-level border.
        SplitMixedQuoteParagraphs(rtb.Document);

        // Restore caret into the (possibly moved) anchor Run.
        if (anchorRun?.Parent is Paragraph)
        {
            var restored = anchorRun.ContentStart.GetPositionAtOffset(anchorOffset, LogicalDirection.Forward);
            if (restored != null)
                rtb.CaretPosition = restored;
        }

        // Save caret for the formatting pass (structure is now stable).
        var caretPara = rtb.CaretPosition.Paragraph;
        int caretCharOffset = caretPara != null
            ? GetCharOffsetInParagraph(caretPara, rtb.CaretPosition)
            : -1;
        bool needCaretRestore = false;

        foreach (var para in rtb.Document.Blocks.OfType<Paragraph>().ToList())
        {
            // Merge stale split Runs from previous passes.
            MergeAdjacentRuns(para);

            bool didSplit = ApplyToParagraph(para);

            if (didSplit && para == caretPara)
                needCaretRestore = true;
        }

        if (needCaretRestore && caretPara != null && caretCharOffset >= 0)
            rtb.CaretPosition = GetPositionAtCharOffset(caretPara, caretCharOffset);
    }

    /// <summary>
    /// When a single Paragraph contains both quoted ("> …") and non-quoted lines
    /// joined by LineBreak, splits it into separate Paragraphs at each transition
    /// so each group can receive its own paragraph-level border independently.
    /// </summary>
    private static void SplitMixedQuoteParagraphs(FlowDocument doc)
    {
        foreach (var para in doc.Blocks.OfType<Paragraph>().ToList())
        {
            // Build lines (groups of inlines between LineBreaks).
            var lines = new List<List<Inline>> { new() };
            var lineBreaks = new List<LineBreak>();
            foreach (var inline in para.Inlines.ToList())
            {
                if (inline is LineBreak lb)
                {
                    lineBreaks.Add(lb);
                    lines.Add(new());
                }
                else
                    lines[^1].Add(inline);
            }
            if (lines.Count <= 1) continue;

            // Mark fenced code regions so they're never treated as quotes.
            var fenced = new bool[lines.Count];
            var fenceIndices = new List<int>();
            for (int i = 0; i < lines.Count; i++)
            {
                string text = string.Concat(lines[i].OfType<Run>().Select(r => r.Text)).Trim();
                if (IsFenceDelimiter(text))
                    fenceIndices.Add(i);
            }
            for (int f = 0; f + 1 < fenceIndices.Count; f += 2)
                for (int j = fenceIndices[f]; j <= fenceIndices[f + 1]; j++)
                    fenced[j] = true;
            if (fenceIndices.Count % 2 == 1)
                fenced[fenceIndices[^1]] = true;

            // Classify each line as quoted or not.
            bool[] quoted = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (fenced[i]) continue;
                string text = string.Concat(lines[i].OfType<Run>().Select(r => r.Text));
                quoted[i] = text.StartsWith("> ");
            }

            // Find transitions.
            var splits = new List<int>();
            for (int i = 1; i < quoted.Length; i++)
                if (quoted[i] != quoted[i - 1])
                    splits.Add(i);
            if (splits.Count == 0) continue;

            // Split in reverse order so earlier indices stay stable.
            for (int s = splits.Count - 1; s >= 0; s--)
            {
                int splitAt = splits[s];

                var newPara = new Paragraph { Margin = para.Margin };
                for (int li = splitAt; li < lines.Count; li++)
                {
                    if (li > splitAt)
                    {
                        var innerLB = lineBreaks[li - 1];
                        para.Inlines.Remove(innerLB);
                        newPara.Inlines.Add(innerLB);
                    }
                    foreach (var inline in lines[li])
                    {
                        para.Inlines.Remove(inline);
                        newPara.Inlines.Add(inline);
                    }
                }
                // Remove the transition LineBreak (replaced by paragraph break).
                para.Inlines.Remove(lineBreaks[splitAt - 1]);
                doc.Blocks.InsertAfter(para, newPara);

                // Shrink data structures for remaining iterations.
                lines = lines.GetRange(0, splitAt);
                lineBreaks = lineBreaks.GetRange(0, splitAt - 1);
                quoted = quoted[..splitAt];
            }
        }
    }

    /// <summary>
    /// Returns true if the given caret position is inside an unclosed ``` fenced
    /// code block, by counting fence delimiter lines (segments separated by LineBreak)
    /// before the caret. Odd count = inside a code block.
    /// </summary>
    public static bool IsCaretInFencedBlock(RichTextBox rtb)
    {
        var para = rtb.CaretPosition.Paragraph;
        if (para is null) return false;

        var caret = rtb.CaretPosition;
        int fenceCount = 0;
        var lineText = new StringBuilder();

        foreach (var inline in para.Inlines)
        {
            if (inline.ElementStart.CompareTo(caret) >= 0)
                break;

            if (inline is LineBreak)
            {
                if (IsFenceDelimiter(lineText.ToString().Trim()))
                    fenceCount++;
                lineText.Clear();
            }
            else if (inline is Run run)
            {
                lineText.Append(run.Text);
            }
        }

        if (IsFenceDelimiter(lineText.ToString().Trim()))
            fenceCount++;

        return fenceCount % 2 == 1;
    }

    private static bool IsFenceDelimiter(string text)
    {
        if (!text.StartsWith("```")) return false;
        string rest = text[3..];
        return rest.Length == 0 || rest.All(char.IsLetterOrDigit);
    }

    /// <summary>
    /// Merges consecutive Runs back into a single Run per segment.
    /// Cleans up stale splits from previous formatting passes so we start fresh.
    /// </summary>
    private static void MergeAdjacentRuns(Paragraph para)
    {
        var inlines = para.Inlines.ToList();
        Run? current = null;
        foreach (var inline in inlines)
        {
            if (inline is Run run)
            {
                if (current != null)
                {
                    current.Text += run.Text;
                    para.Inlines.Remove(run);
                }
                else
                {
                    current = run;
                }
            }
            else
            {
                current = null;
            }
        }
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

    /// <summary>Returns true if Runs were split (caret may need restoring).</summary>
    private bool ApplyToParagraph(Paragraph para)
    {
        // Reset all Run formatting and paragraph-level quote styling to defaults.
        foreach (var run in para.Inlines.OfType<Run>())
        {
            run.FontWeight = FontWeights.Normal;
            run.FontStyle = FontStyles.Normal;
            run.TextDecorations = null;
            run.Foreground = _defaultForeground;
            run.FontFamily = para.FontFamily;
        }
        para.BorderThickness = new Thickness(0);
        para.Padding = new Thickness(0);

        var segments = BuildTextSegments(para);

        // ── Fenced code blocks across segments (LineBreak-separated lines) ──
        // Detect ``` fence delimiters at the segment level, since Shift+Enter
        // creates LineBreak inlines within a single paragraph.
        var fenceSegIndices = new List<int>();
        for (int si = 0; si < segments.Count; si++)
        {
            if (IsFenceDelimiter(segments[si].Text.Trim()))
                fenceSegIndices.Add(si);
        }

        var fencedContent = new HashSet<int>();
        var fenceDelimSegs = new HashSet<int>();
        for (int f = 0; f + 1 < fenceSegIndices.Count; f += 2)
        {
            int open = fenceSegIndices[f];
            int close = fenceSegIndices[f + 1];
            fenceDelimSegs.Add(open);
            fenceDelimSegs.Add(close);
            for (int j = open + 1; j < close; j++)
                fencedContent.Add(j);
        }
        if (fenceSegIndices.Count % 2 == 1)
            fenceDelimSegs.Add(fenceSegIndices[^1]);

        // ── Blockquote segments (lines starting with "> ") ─────────────────
        // After SplitMixedQuoteParagraphs, each paragraph is homogeneous
        // (all-quoted or all-unquoted), so the paragraph border is safe.
        var quoteSegs = new HashSet<int>();
        for (int si = 0; si < segments.Count; si++)
        {
            if (fenceDelimSegs.Contains(si) || fencedContent.Contains(si)) continue;
            if (string.IsNullOrEmpty(segments[si].Text)) continue;
            if (segments[si].Text.StartsWith("> "))
                quoteSegs.Add(si);
        }

        // ── Apply styles per segment ────────────────────────────────────────
        bool didSplit = false;

        for (int si = 0; si < segments.Count; si++)
        {
            var seg = segments[si];

            if (fenceDelimSegs.Contains(si))
            {
                // Mute the ``` delimiter line.
                foreach (var ri in seg.Runs)
                    ri.Run.Foreground = _mutedBrush;
                continue;
            }

            if (fencedContent.Contains(si))
            {
                // Code font for fenced content, no inline formatting.
                foreach (var ri in seg.Runs)
                    ri.Run.FontFamily = ConsolasFont;
                continue;
            }

            if (string.IsNullOrEmpty(seg.Text)) continue;

            bool isQuote = quoteSegs.Contains(si);

            var spans = Parse(seg.Text);

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

            // For quote segments, always split out the "> " prefix.
            if (isQuote)
                boundaries.Add(2);

            if (boundaries.Count == 0) continue;

            // Split the single merged Run at boundaries so each piece falls
            // entirely within one style region.
            didSplit |= SplitRunAtBoundaries(para, seg, boundaries);

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

                // Hide the "> " quote prefix.
                if (isQuote && runStart == 0 && runEnd <= 2)
                {
                    ri.Run.Foreground = Brushes.Transparent;
                    continue;
                }

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

        // ── Paragraph-level quote bar ────────────────────────────────────────
        if (quoteSegs.Count > 0)
        {
            para.BorderBrush = _mutedBrush;
            para.BorderThickness = new Thickness(3, 0, 0, 0);
            para.Padding = new Thickness(0);
        }

        return didSplit;
    }

    /// <summary>
    /// Splits the (already-merged) Run at the given boundaries.
    /// Keeps the original Run as the first piece and inserts new Runs after it
    /// so the caret stays in the original Run when possible.
    /// Returns true if any Runs were actually split.
    /// </summary>
    private static bool SplitRunAtBoundaries(Paragraph para, TextSegment seg, SortedSet<int> boundaries)
    {
        bool anySplit = false;
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
            anySplit = true;

            // Keep the original Run as the first piece.
            // Insert remaining pieces after it.
            string originalText = run.Text;
            run.Text = originalText[..splitPoints[0]];

            var anchor = run;
            int prev = splitPoints[0];
            for (int i = 1; i < splitPoints.Count; i++)
            {
                var newRun = new Run(originalText[prev..splitPoints[i]])
                {
                    Foreground = run.Foreground,
                    FontWeight = run.FontWeight,
                    FontStyle = run.FontStyle,
                    FontFamily = run.FontFamily,
                };
                para.Inlines.InsertAfter(anchor, newRun);
                anchor = newRun;
                prev = splitPoints[i];
            }

            // Final piece
            if (prev < originalText.Length)
            {
                var lastRun = new Run(originalText[prev..])
                {
                    Foreground = run.Foreground,
                    FontWeight = run.FontWeight,
                    FontStyle = run.FontStyle,
                    FontFamily = run.FontFamily,
                };
                para.Inlines.InsertAfter(anchor, lastRun);
            }
        }
        return anySplit;
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
        // Triple backtick inline spans
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
                else
                {
                    // No closing ``` — mask these backticks so the single-backtick
                    // pass doesn't pair them individually.
                    MarkRange(codeMask, i, 3);
                    i += 3;
                }
            }
            else i++;
        }

        // Single backtick inline spans
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
    /// Counts text characters from the paragraph start to the given position.
    /// Walks inlines manually so the count is stable across Run splits
    /// (counts content chars, not structural symbols).
    /// </summary>
    private static int GetCharOffsetInParagraph(Paragraph para, TextPointer position)
    {
        int offset = 0;
        foreach (var inline in para.Inlines)
        {
            if (inline is Run run)
            {
                if (position.CompareTo(run.ContentStart) < 0)
                    return offset;
                if (position.CompareTo(run.ContentEnd) <= 0)
                    return offset + run.ContentStart.GetOffsetToPosition(position);
                offset += run.Text.Length;
            }
            else if (inline is InlineUIContainer)
            {
                if (position.CompareTo(inline.ElementEnd) <= 0)
                    return offset;
                offset++;
            }
        }
        return offset;
    }

    /// <summary>
    /// Walks the paragraph's inlines counting text characters (and 1 per InlineUIContainer)
    /// to find the TextPointer at the given character offset.
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
