using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace ToolBox.Basics.Tests;

/// <summary>
/// "Descriptions are prompts" (plan 001, tool design rule 3) — encoded as an executable
/// rule instead of a hope. The model never sees the C# code; if a tool or parameter
/// ships without a description, the model is flying blind. This test makes that a
/// build failure rather than a mystery in production.
/// </summary>
public class DescriptionConventionTests
{
    private static MethodInfo[] ToolMethods() =>
        [.. typeof(BasicsTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    [Fact]
    public void ToolsetExposesExpectedToolCount()
        => Assert.Equal(3, ToolMethods().Length);

    [Fact]
    public void EveryTool_HasANonEmptyDescription()
    {
        foreach (MethodInfo tool in ToolMethods())
        {
            var description = tool.GetCustomAttribute<DescriptionAttribute>();
            Assert.False(
                string.IsNullOrWhiteSpace(description?.Description),
                $"Tool method '{tool.Name}' is missing a [Description] — the model cannot reason about an undescribed tool.");
        }
    }

    [Fact]
    public void EveryToolParameter_HasANonEmptyDescription()
    {
        foreach (MethodInfo tool in ToolMethods())
        {
            foreach (ParameterInfo parameter in tool.GetParameters())
            {
                var description = parameter.GetCustomAttribute<DescriptionAttribute>();
                Assert.False(
                    string.IsNullOrWhiteSpace(description?.Description),
                    $"Parameter '{parameter.Name}' of tool '{tool.Name}' is missing a [Description].");
            }
        }
    }
}
