using System.Text.Json.Nodes;
using ToolBox.Core.Desired;

namespace ToolBox.Core.Tests;

public class DesiredStateMigratorTests
{
    private static MigrationResult Run(string json, DesiredStateMigrator? m = null) =>
        (m ?? new DesiredStateMigrator()).Migrate(JsonNode.Parse(json));

    [Fact]
    public void Current_version_passes_through()
    {
        var r = Run("""{ "schemaVersion": 1, "toolsets": {} }""");

        Assert.Equal(MigrationOutcome.Ok, r.Outcome);
        Assert.NotNull(r.Document);
    }

    [Fact]
    public void Newer_version_is_unsupported_and_reports_the_version()
    {
        var r = Run("""{ "schemaVersion": 99 }""");

        Assert.Equal(MigrationOutcome.Unsupported, r.Outcome);
        Assert.Equal(99, r.FoundVersion);
        Assert.Null(r.Document);
    }

    [Theory]
    [InlineData("""{ "toolsets": {} }""")]
    [InlineData("""{ "schemaVersion": "1" }""")]
    [InlineData("""{ "schemaVersion": 1.5 }""")]
    [InlineData("""{ "schemaVersion": null }""")]
    [InlineData("""{ "schemaVersion": 0 }""")]
    [InlineData("""{ "schemaVersion": -3 }""")]
    [InlineData("""[1, 2]""")]
    [InlineData("""42""")]
    public void Missing_non_integer_or_low_version_is_corrupt(string json)
    {
        Assert.Equal(MigrationOutcome.Corrupt, Run(json).Outcome);
    }

    [Fact]
    public void Null_document_is_corrupt()
    {
        Assert.Equal(MigrationOutcome.Corrupt, new DesiredStateMigrator().Migrate(null).Outcome);
    }

    [Fact]
    public void Migration_chain_runs_each_step_in_order_and_stamps_the_version()
    {
        var steps = new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [1] = d => { d["trail"] = "1;"; return d; },
            [2] = d => { d["trail"] = d["trail"]!.GetValue<string>() + "2;"; return d; },
        };

        var r = Run("""{ "schemaVersion": 1 }""", new DesiredStateMigrator(3, steps));

        Assert.Equal(MigrationOutcome.Ok, r.Outcome);
        Assert.Equal("1;2;", r.Document!["trail"]!.GetValue<string>());
        Assert.Equal(3, r.Document["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Missing_migration_step_is_corrupt()
    {
        var r = Run("""{ "schemaVersion": 1 }""", new DesiredStateMigrator(2));

        Assert.Equal(MigrationOutcome.Corrupt, r.Outcome);
    }
}
