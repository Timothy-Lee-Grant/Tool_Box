namespace ToolBox.E2E.Tests;

/// <summary>
/// A fact that only runs when <c>TOOLBOX_E2E=1</c>, so a plain <c>dotnet test</c> stays fast and needs no
/// Docker or child processes. CI selects these with <c>--filter "Category=E2E"</c> and sets the variable.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EFactAttribute : FactAttribute
{
    public const string EnvVar = "TOOLBOX_E2E";

    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1")
            Skip = $"E2E test: set {EnvVar}=1 to run.";
    }
}
