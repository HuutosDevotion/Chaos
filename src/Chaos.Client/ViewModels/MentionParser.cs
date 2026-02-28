using System.Text.RegularExpressions;

namespace Chaos.Client.ViewModels;

public record MentionSegment(string Text, bool IsMention);

public static class MentionParser
{
    private static readonly Regex MentionRegex = new(@"@(\w+)", RegexOptions.Compiled);

    public static List<MentionSegment> Parse(string content, IEnumerable<string>? validUsernames = null)
    {
        var segments = new List<MentionSegment>();
        if (string.IsNullOrEmpty(content))
            return segments;

        var usernameSet = validUsernames is not null
            ? new HashSet<string>(validUsernames, StringComparer.OrdinalIgnoreCase)
            : null;

        int lastIndex = 0;
        foreach (Match match in MentionRegex.Matches(content))
        {
            var username = match.Groups[1].Value;
            bool isMention = usernameSet is null || usernameSet.Contains(username);

            if (match.Index > lastIndex)
                segments.Add(new MentionSegment(content[lastIndex..match.Index], false));

            segments.Add(new MentionSegment(match.Value, isMention));
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
            segments.Add(new MentionSegment(content[lastIndex..], false));

        return segments;
    }
}
