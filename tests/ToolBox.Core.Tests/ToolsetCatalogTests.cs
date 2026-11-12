using Microsoft.Extensions.DependencyInjection;
using ToolBox.Core;

namespace ToolBox.Core.Tests;

public class ToolsetCatalogTests
{
    private static ToolsetCatalogEntry Entry(string id, List<string> log) =>
        new(id, id.ToUpperInvariant(), $"{id} toolset", _ => log.Add(id));

    [Fact]
    public void DuplicateId_Throws()
    {
        var log = new List<string>();
        Assert.Throws<ArgumentException>(() =>
            new ToolsetCatalog([Entry("a", log), Entry("A", log)]));
    }

    [Fact]
    public void BlankId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ToolsetCatalog([Entry(" ", new List<string>())]));
    }

    [Fact]
    public void ComposeAll_RegistersEveryEntry_AndReturnsTheirDescriptors()
    {
        var log = new List<string>();
        var catalog = new ToolsetCatalog([
            new ToolsetCatalogEntry("a", "A", "a", s => { log.Add("a"); s.AddSingleton<string>("x"); }),
            Entry("b", log)]);

        var added = catalog.ComposeAll(new ServiceCollection());

        Assert.Equal(["a", "b"], log);
        Assert.Single(added["a"]);
        Assert.Empty(added["b"]);
    }

    [Fact]
    public void ValidateIds_UnknownThrows_AndListsValidIds_KnownIsCaseInsensitive()
    {
        var catalog = new ToolsetCatalog([Entry("a", []), Entry("b", [])]);

        catalog.ValidateIds(["A", " b "]);
        var ex = Assert.Throws<InvalidOperationException>(() => catalog.ValidateIds(["a", "nope"]));

        Assert.Contains("nope", ex.Message);
        Assert.Contains("a, b", ex.Message);
    }
}
