using LibreLancer.ContentEdit;
using LibreLancer.Data.Ini;
using LibreLancer.Data.Schema.Missions;
using LibreLancer.Missions;
using LibreLancer.Net;
using Xunit;

namespace LibreLancer.Tests;

// The objective type base (OL-0039). Retail reads "type = base, <base>, <text id>". Value 1 is the nickname or the
// number of a base, and value 2 is the text of the objective. The engine did not know the type, so it logged an
// error and the objective had no text.
[Collection(nameof(LogCapture))]
public class NNObjectiveBaseTests
{
    private const string BaseObjective = """
        [NNObjective]
        nickname = sample_objective
        state = HIDDEN
        type = base, sample_base, 12345
        """;

    private static NNObjective Parse(string text)
    {
        Assert.True(NNObjective.TryParse(IniText.Section(text), out var objective));
        return objective;
    }

    [Fact]
    public void TheTypeBaseReadsTheBaseAndTheText()
    {
        using var log = new LogCapture();
        var objective = Parse(BaseObjective);

        Assert.Equal(NNObjectiveType.@base, objective.Type);
        Assert.Equal("sample_base", objective.Base);
        Assert.Equal(12345, objective.NameIds);
        Assert.Empty(log.Lines(IniText.File));
    }

    [Fact]
    public void ABaseLineWithoutTheTextGivesOneWarning()
    {
        using var log = new LogCapture();
        var objective = Parse("""
            [NNObjective]
            nickname = sample_objective
            type = base, sample_base
            """);

        Assert.Equal(NNObjectiveType.@base, objective.Type);
        Assert.Equal("", objective.Base);
        Assert.Single(log.Lines("base needs 3 entries, got 2"));
    }

    [Fact]
    public void AnObjectiveOfTypeBaseShowsItsText()
    {
        var objective = MissionRuntime.ToNetObjective(Parse(BaseObjective));

        Assert.Equal(ObjectiveKind.Basic, objective.Kind);
        Assert.Equal(12345, objective.Ids);
    }

    [Fact]
    public void TheEditorWritesTheBaseLineAgain()
    {
        var builder = new IniBuilder();
        IniSerializer.SerializeMissionObjective(Parse(BaseObjective), builder);

        Assert.True(NNObjective.TryParse(Assert.Single(builder.Sections), out var copy));
        Assert.Equal((NNObjectiveType.@base, "sample_base", 12345), (copy.Type, copy.Base, copy.NameIds));
    }
}
