using ToolBox.Core.Health;
using ToolBox.Core.Toolsets;

namespace ToolBox.Core.Tests;

public class ToolsetGateTests
{
    [Fact]
    public void DefaultsToClosed()
    {
        var gate = new ToolsetGate();
        Assert.False(gate.IsOn("voxel"));
        Assert.Empty(gate.Snapshot());
    }

    [Fact]
    public void OpenThenClose()
    {
        var gate = new ToolsetGate();
        gate.Open("voxel");
        Assert.True(gate.IsOn("voxel"));
        gate.Close("voxel");
        Assert.False(gate.IsOn("voxel"));
    }

    [Fact]
    public void IdsAreCaseInsensitive()
    {
        var gate = new ToolsetGate();
        gate.Open("Voxel");
        Assert.True(gate.IsOn("VOXEL"));
        gate.Close("voxel");
        Assert.False(gate.IsOn("Voxel"));
    }

    [Fact]
    public void SnapshotIsSortedAndIsolated()
    {
        var gate = new ToolsetGate();
        gate.Open("voxel");
        gate.Open("basics");
        var snap = gate.Snapshot();
        gate.Close("basics");
        Assert.Equal(new[] { "basics", "voxel" }, snap);
        Assert.Equal(new[] { "voxel" }, gate.Snapshot());
    }

    [Fact]
    public async Task ConcurrentOpenCloseEndsConsistent()
    {
        var gate = new ToolsetGate();
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            gate.Open($"t{i}");
            if (i % 2 == 0) gate.Close($"t{i}");
        })));
        Assert.Equal(25, gate.Snapshot().Count);
    }

    [Fact]
    public void OffMessageNamesTheToolset() =>
        Assert.Equal("toolset 'voxel' is off", ToolsetGate.OffMessage("voxel"));

    [Fact]
    public void StatusBoardUnknownIsOff() =>
        Assert.Equal(ToolsetStatus.Off, new ToolsetStatusBoard().Get("x").Status);

    [Fact]
    public void ComponentStatusRegistryRemoveStopsReporting()
    {
        var r = new ComponentStatusRegistry();
        r.Set("viewer", "ok");
        r.Remove("viewer");
        r.Remove("never-there");
        Assert.Empty(r.Snapshot());
    }
}
