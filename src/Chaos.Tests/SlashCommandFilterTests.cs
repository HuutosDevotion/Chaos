using Chaos.Shared;
using Xunit;

namespace Chaos.Tests;

public class SlashCommandFilterTests
{
    private static readonly List<SlashCommandDto> Commands =
    [
        new() { Name = "roll",  Description = "Roll a die",   Usage = "/roll <die>" },
        new() { Name = "shrug", Description = "Append shrug", Usage = "/shrug [message]" },
    ];

    // ── no suggestions ────────────────────────────────────────────────────────

    [Fact]
    public void EmptyInput_ReturnsNoSuggestions()
    {
        Assert.Empty(SlashCommandFilter.Filter(Commands, ""));
    }

    [Fact]
    public void NullInput_ReturnsNoSuggestions()
    {
        Assert.Empty(SlashCommandFilter.Filter(Commands, null!));
    }

    [Fact]
    public void NonSlashInput_ReturnsNoSuggestions()
    {
        Assert.Empty(SlashCommandFilter.Filter(Commands, "hello"));
    }

    // ── prefix matching (no space yet) ────────────────────────────────────────

    [Fact]
    public void SlashAlone_MatchesAllCommands()
    {
        var results = SlashCommandFilter.Filter(Commands, "/").ToList();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void PartialVerb_MatchesByPrefix()
    {
        var results = SlashCommandFilter.Filter(Commands, "/ro").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }

    [Fact]
    public void FullVerbNoSpace_StillPrefixMatches()
    {
        var results = SlashCommandFilter.Filter(Commands, "/roll").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }

    [Fact]
    public void PartialVerb_MatchesMultipleCommands()
    {
        var cmds = Commands.Append(new SlashCommandDto { Name = "rotate" }).ToList();
        var results = SlashCommandFilter.Filter(cmds, "/ro").ToList();
        Assert.Equal(2, results.Count);
        Assert.Contains(results, c => c.Name == "roll");
        Assert.Contains(results, c => c.Name == "rotate");
    }

    // ── exact matching (space present) ────────────────────────────────────────

    [Fact]
    public void VerbWithSpace_ExactlyMatchesCommand()
    {
        var results = SlashCommandFilter.Filter(Commands, "/roll ").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }

    [Fact]
    public void VerbWithSpaceAndArgs_ExactlyMatchesCommand()
    {
        var results = SlashCommandFilter.Filter(Commands, "/roll d20").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }

    [Fact]
    public void VerbWithSpace_NoPartialMatch()
    {
        // "/ro " has a space so it's an exact match — "ro" != "roll"
        Assert.Empty(SlashCommandFilter.Filter(Commands, "/ro "));
    }

    [Fact]
    public void UnknownVerbWithSpace_ReturnsNoSuggestions()
    {
        Assert.Empty(SlashCommandFilter.Filter(Commands, "/unknown "));
    }

    // ── case insensitivity ────────────────────────────────────────────────────

    [Fact]
    public void PrefixMatch_IsCaseInsensitive()
    {
        var results = SlashCommandFilter.Filter(Commands, "/RO").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }

    [Fact]
    public void ExactMatch_IsCaseInsensitive()
    {
        var results = SlashCommandFilter.Filter(Commands, "/ROLL ").ToList();
        Assert.Single(results);
        Assert.Equal("roll", results[0].Name);
    }
}
