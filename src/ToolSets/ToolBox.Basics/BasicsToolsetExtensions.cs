using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core.DependencyInjection;

namespace ToolBox.Basics;

/// <summary>
/// The toolset's single public doorway (plan 001, Step 2.4 convention).
/// The Host composes this toolset with exactly one line and learns nothing
/// about the tool types inside.
/// </summary>
public static class BasicsToolsetExtensions
{
    public static IMcpServerBuilder AddBasicsToolset(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddToolsetDescriptor(
            name: "Basics",
            description: "Trivial connectivity and identity tools: ping, server_info, current_time.");

        return builder.WithTools<BasicsTools>();
    }
}
