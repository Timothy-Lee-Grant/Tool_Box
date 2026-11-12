using Microsoft.Extensions.Configuration;
using ToolBox.Host;

namespace ToolBox.Host.Tests;

public class HostOriginPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1", 8095, true)]
    [InlineData("localhost", 8095, true)]
    [InlineData("LocalHost", 8095, true)]
    [InlineData("[::1]", 8095, true)]
    [InlineData("evil.example", 8095, false)]
    [InlineData("localhost", 9999, false)]
    [InlineData("localhost", null, false)]
    public void Strict_port_policy_matches_the_admin_rules(string host, int? port, bool expected) =>
        Assert.Equal(expected, new HostOriginPolicy(strictPort: true).IsAllowedHost(host, port, 8095));

    [Theory]
    [InlineData("localhost", 8081, true)]
    [InlineData("localhost.", 8080, true)]
    [InlineData("toolbox", 8080, true)]
    [InlineData("TOOLBOX", null, true)]
    [InlineData("evil.example", 8080, false)]
    [InlineData("evil.example", 8081, false)]
    public void Hostname_only_policy_ignores_the_port(string host, int? port, bool expected) =>
        Assert.Equal(expected, new HostOriginPolicy(["toolbox"], strictPort: false).IsAllowedHost(host, port, 8080));

    [Theory]
    [InlineData("http://localhost:8095", true)]
    [InlineData("http://127.0.0.1:8095", true)]
    [InlineData("http://localhost:6274", false)]
    [InlineData("https://localhost:8095", false)]
    [InlineData("http://evil.example:8095", false)]
    [InlineData("null", false)]
    public void Exact_origin_requires_http_loopback_and_the_port(string origin, bool expected) =>
        Assert.Equal(expected, HostOriginPolicy.IsExactLoopbackOrigin(origin, 8095));

    [Theory]
    [InlineData("http://localhost:6274", true)]
    [InlineData("https://localhost:6274", true)]
    [InlineData("http://[::1]:3000", true)]
    [InlineData("http://evil.example", false)]
    [InlineData("null", false)]
    public void Loose_origin_allows_loopback_on_any_port(string origin, bool expected) =>
        Assert.Equal(expected, HostOriginPolicy.IsAllowedOrigin(origin));

    [Fact]
    public void Configured_origin_is_allowed() =>
        Assert.True(HostOriginPolicy.IsAllowedOrigin("https://ui.example", ["https://ui.example/"]));

    private static McpGuardOptions Resolve(Dictionary<string, string?> values, string? urls = "http://127.0.0.1:8080") =>
        McpGuardOptions.Resolve(new ConfigurationBuilder().AddInMemoryCollection(values).Build(), urls);

    [Fact]
    public void List_syntax_binds()
    {
        var o = Resolve(new() { ["Security:Mcp:AllowedHosts:0"] = "toolbox", ["Security:Mcp:AllowedHosts:1"] = "box.lan" });
        Assert.Equal(["toolbox", "box.lan"], o.AllowedHosts);
        Assert.False(o.UsedLegacyHostsKey);
    }

    [Fact]
    public void Env_var_double_underscore_list_form_binds()
    {
        // The real env provider turns "__" into ":" - this is the syntax docker-compose will use.
        const string name = "TOOLBOX_Security__Mcp__AllowedHosts__0";
        Environment.SetEnvironmentVariable(name, "toolbox");
        try
        {
            var env = new ConfigurationBuilder().AddEnvironmentVariables(prefix: "TOOLBOX_").Build();
            Assert.Equal(["toolbox"], McpGuardOptions.Resolve(env, null).AllowedHosts);
        }
        finally { Environment.SetEnvironmentVariable(name, null); }
    }

    [Fact]
    public void Semicolon_string_is_accepted()
    {
        var o = Resolve(new() { ["Security:Mcp:AllowedHosts"] = "a;b" });
        Assert.Equal(["a", "b"], o.AllowedHosts);
    }

    [Fact]
    public void Legacy_AllowedHosts_is_the_fallback()
    {
        var o = Resolve(new() { ["AllowedHosts"] = "localhost;toolbox" });
        Assert.True(o.UsedLegacyHostsKey);
        Assert.Equal(["localhost", "toolbox"], o.AllowedHosts);
    }

    [Fact]
    public void New_key_wins_over_legacy()
    {
        var o = Resolve(new() { ["AllowedHosts"] = "old", ["Security:Mcp:AllowedHosts:0"] = "new" });
        Assert.Equal(["new"], o.AllowedHosts);
        Assert.False(o.UsedLegacyHostsKey);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    public void Legacy_star_or_empty_is_ignored(string legacy)
    {
        var o = Resolve(new() { ["AllowedHosts"] = legacy });
        Assert.Empty(o.AllowedHosts);
        Assert.False(o.UsedLegacyHostsKey);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080", false)]
    [InlineData("http://localhost:8080", false)]
    [InlineData("http://[::1]:8080", false)]
    [InlineData("http://0.0.0.0:8080", true)]
    [InlineData("http://+:8080", true)]
    [InlineData("http://*:8080", true)]
    [InlineData("http://192.168.1.5:8080", true)]
    public void Non_loopback_bind_detection(string url, bool expected) =>
        Assert.Equal(expected, Resolve([], url).BindsNonLoopback);

    [Fact]
    public void Warning_needed_only_when_non_loopback_with_defaults_only()
    {
        Assert.True(Resolve([], "http://+:8080").NeedsAllowlistWarning);
        Assert.False(Resolve(new() { ["Security:Mcp:AllowedHosts:0"] = "toolbox" }, "http://+:8080").NeedsAllowlistWarning);
        Assert.False(Resolve([], "http://127.0.0.1:8080").NeedsAllowlistWarning);
    }
}
