using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolBox.Core.Desired;

/// <summary>Shared serializer options so every reader and writer of the file agrees on the format.</summary>
public static class DesiredStateJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        // Hand-editing is allowed (but unsupported), so be forgiving on read. Comments are lost on the next save.
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
