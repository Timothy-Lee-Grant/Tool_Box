using System.Text.Json;
using ToolBox.Core.Desired;

namespace ToolBox.Core.Tests;

public class DesiredStateModelTests
{
    private const string Minimal = """
        {
          "schemaVersion": 1,
          "toolsets": {
            "basics":   { "enabled": true },
            "voxel":    { "enabled": true,  "settings": { "autoOpenViewer": false } },
            "embedded": { "enabled": false, "settings": { "serialPort": "COM3" } }
          }
        }
        """;

    private static DesiredState Read(string json) =>
        JsonSerializer.Deserialize<DesiredState>(json, DesiredStateJson.Options)!;

    private static string Write(DesiredState s) => JsonSerializer.Serialize(s, DesiredStateJson.Options);

    [Fact]
    public void Minimal_file_round_trips()
    {
        var second = Read(Write(Read(Minimal)));

        Assert.Equal(1, second.SchemaVersion);
        Assert.Equal(3, second.Toolsets.Count);
        Assert.True(second.Toolsets["basics"].Enabled);
        Assert.False(second.Toolsets["embedded"].Enabled);
        Assert.False(second.Toolsets["voxel"].Settings!["autoOpenViewer"].GetBoolean());
        Assert.Equal("COM3", second.Toolsets["embedded"].Settings!["serialPort"].GetString());
    }

    [Fact]
    public void Missing_enabled_reads_as_null_and_is_not_written_back()
    {
        var s = Read("""{ "schemaVersion": 1, "toolsets": { "voxel": { "settings": { "a": 1 } } } }""");

        Assert.Null(s.Toolsets["voxel"].Enabled);
        Assert.DoesNotContain("enabled", Write(s));
    }

    [Fact]
    public void Unknown_ids_and_unknown_fields_survive_a_round_trip()
    {
        var s = Read("""
            { "schemaVersion": 1, "futureTop": { "x": 1 },
              "toolsets": { "removed-toolset": { "enabled": true, "futureField": "keep" } } }
            """);

        var again = Read(Write(s));

        Assert.True(again.Toolsets["removed-toolset"].Enabled);
        Assert.Equal("keep", again.Toolsets["removed-toolset"].Extra!["futureField"].GetString());
        Assert.Equal(1, again.Extra!["futureTop"].GetProperty("x").GetInt32());
    }

    [Fact]
    public void Ids_are_case_insensitive_and_comments_and_trailing_commas_are_tolerated()
    {
        var s = Read("""
            { // hand-edited
              "schemaVersion": 1,
              "toolsets": { "Voxel": { "enabled": false, }, },
            }
            """);

        Assert.False(s.Toolsets["voxel"].Enabled);
    }

    [Fact]
    public void Wrong_typed_enabled_throws_so_the_store_can_report_corrupt()
    {
        Assert.Throws<JsonException>(() =>
            Read("""{ "schemaVersion": 1, "toolsets": { "voxel": { "enabled": "yes" } } }"""));
    }
}
