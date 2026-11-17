using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ToolBox.Core.Instance;
using ToolBox.Core.Logging;
using ToolBox.Core.Ports;
using ToolBox.Host;

// ToolBox.Host — thin composition root.
// The Host selects its transport at startup, then hands off to a per-transport path.
// Everything the server IS lives in AddToolBoxServer(); everything below is only
// "which wire, and how to fail loudly".

// ---- Transport selection (bootstrap config, before any host is built) ----
// Precedence (last source added wins): appsettings.json < TOOLBOX_* env var < --transport flag.
// BaseDirectory, not cwd: clients like Claude Desktop launch this DLL from an arbitrary
// working directory, so relative paths would silently miss appsettings.json.
var bootstrap = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables(prefix: "TOOLBOX_")
    .AddCommandLine(args, new Dictionary<string, string> { ["--transport"] = "Transport", ["--mode"] = "Mode" })
    .Build();

string transport = bootstrap["Transport"]?.Trim().ToLowerInvariant() ?? "stdio";

// Background mode (issue #38) is a run profile on top of HTTP: lock + host.json + the usual MCP/admin/reconciler.
bool background = BackgroundMode.IsBackground(bootstrap);
if (background)
{
    // appsettings.json defaults Transport to stdio, so only an EXPLICIT stdio (env var or flag) is a conflict.
    string? explicitTransport = Environment.GetEnvironmentVariable("TOOLBOX_Transport");
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i].Equals("--transport", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) explicitTransport = args[i + 1];
        else if (args[i].StartsWith("--transport=", StringComparison.OrdinalIgnoreCase)) explicitTransport = args[i]["--transport=".Length..];
    }
    if (explicitTransport is not null && explicitTransport.Trim().Equals("stdio", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("--mode background runs over HTTP and cannot be combined with --transport stdio.");
        return 2;
    }
    transport = "http";
}

switch (transport)
{
    case "stdio":
        await RunStdioAsync(args);
        return 0;

    case "http":
        InstanceLock? instanceLock = null;
        try
        {
            if (background)
            {
                string dir = BackgroundMode.InstanceDir(bootstrap);
                try
                {
                    instanceLock = InstanceLock.TryAcquire(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Cannot use the instance folder '{dir}': {ex.Message} Set Instance:Dir (TOOLBOX_Instance__Dir) to a writable folder.");
                    return 1;
                }

                if (instanceLock is null)
                {
                    Console.Error.WriteLine(await BackgroundMode.DescribeRunningAsync(dir));
                    return BackgroundMode.AlreadyRunningExitCode;
                }
            }

            await ToolBoxHttpApp.RunAsync(ToolBoxHttpApp.Build(args));
            return 0;
        }
        catch (PortConflictException ex)
        {
            // One clear line on stderr (ADR-004), then a dedicated exit code (2 is a bad transport).
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        finally
        {
            // Released last: after the reconciler stopped the toolsets and host.json was removed.
            instanceLock?.Dispose();
        }

    default:
        // Fail fast and loud — never guess a transport. (Error text goes to stderr,
        // like everything else that isn't protocol.)
        Console.Error.WriteLine($"Unknown transport '{transport}'. Valid values: stdio, http.");
        return 2;
}

static async Task RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // ADR-004: stdout belongs to the JSON-RPC protocol; all logs go to stderr.
    // Stays FIRST so no later registration can sneak a stdout logger in.
    builder.Logging.UseStderrOnly();

    // Same TOOLBOX_ env-var layer the bootstrap uses, so TOOLBOX_TOOLSETS reaches the catalog.
    builder.Configuration.AddEnvironmentVariables(prefix: "TOOLBOX_");

    // Same lowest-priority Ports defaults as HTTP mode, so the viewer reads one table.
    ToolBox.Host.Ports.PortTable.AddDefaults(builder.Configuration);

    builder.Services
        .AddToolBoxServer(builder.Configuration)
        .WithStdioServerTransport();

    // stdio has one long-lived session, so it can push tools/list_changed (#34); HTTP keeps the no-op.
    builder.Services.Replace(ServiceDescriptor.Singleton<IToolListChangedNotifier, StdioToolListChangedNotifier>());

    await builder.Build().RunAsync();
}
