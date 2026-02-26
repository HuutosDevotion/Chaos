namespace Chaos.Shared;

public static class SlashCommandFilter
{
    /// <summary>
    /// Returns commands that match the current input string.
    /// While typing the verb (no space yet): prefix match.
    /// Once a space is typed: exact match on the verb (so hints stay visible while typing args).
    /// </summary>
    public static IEnumerable<SlashCommandDto> Filter(IEnumerable<SlashCommandDto> commands, string input)
    {
        if (string.IsNullOrEmpty(input) || input[0] != '/')
            return [];

        var afterSlash = input.Length > 1 ? input[1..] : string.Empty;
        var spaceIdx = afterSlash.IndexOf(' ');
        var verb = spaceIdx >= 0 ? afterSlash[..spaceIdx] : afterSlash;
        var hasArgs = spaceIdx >= 0;

        return hasArgs
            ? commands.Where(c => c.Name.Equals(verb, StringComparison.OrdinalIgnoreCase))
            : commands.Where(c => c.Name.StartsWith(verb, StringComparison.OrdinalIgnoreCase));
    }
}
