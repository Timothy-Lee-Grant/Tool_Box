using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using ToolBox.Embedded;

namespace ToolBox.Embedded.Tests;

/// <summary>
/// "Descriptions are prompts" (plan 001, tool design rule 3) — same reflection
/// enforcement as every other toolset's DescriptionConventionTests. Covers
/// BuildTools only so far (plan 006 Step 3); the count assertion is meant to
/// force a deliberate update, not silently pass, as later steps add more
/// [McpServerToolType] classes.
/// </summary>
public class DescriptionConventionTests
{
    private static MethodInfo[] ToolMethods() =>
        [.. typeof(BuildTools)
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
