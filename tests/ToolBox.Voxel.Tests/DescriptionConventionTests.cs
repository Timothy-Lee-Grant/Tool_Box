using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace ToolBox.Voxel.Tests;

/// <summary>
/// "Descriptions are prompts" (plan 001, tool design rule 3), enforced here exactly
/// as it is for Basics — the model never sees this C# code; an undescribed tool or
/// parameter is the model flying blind, made a build failure instead of a mystery.
/// </summary>
public class DescriptionConventionTests
{
    private static MethodInfo[] ToolMethods() =>
        [.. typeof(VoxelTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)];

    [Fact]
    public void ToolsetExposesExpectedToolCount()
        => Assert.Equal(12, ToolMethods().Length);

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

    [Fact]
    public void VoxelPathPoint_AndItsProperties_HaveDescriptions()
    {
        // The one non-primitive parameter shape in this toolset (place_tube's path) —
        // its own [Description] and each property's flow into the generated JSON
        // schema exactly like a top-level parameter's does (verified against the
        // actual SDK schema output during Step 3 design).
        var typeDescription = typeof(VoxelPathPoint).GetCustomAttribute<DescriptionAttribute>();
        Assert.False(string.IsNullOrWhiteSpace(typeDescription?.Description));

        foreach (PropertyInfo property in typeof(VoxelPathPoint).GetProperties())
        {
            var description = property.GetCustomAttribute<DescriptionAttribute>();
            Assert.False(
                string.IsNullOrWhiteSpace(description?.Description),
                $"Property '{property.Name}' of VoxelPathPoint is missing a [Description].");
        }
    }
}
