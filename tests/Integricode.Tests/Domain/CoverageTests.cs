using Integricode.Domain;

namespace Integricode.Tests.Domain;

public class CoverageTests
{
    [Fact]
    public void Factories_set_state_and_reasons()
    {
        Assert.Equal(CoverageState.Complete, Coverage.Complete.State);
        Assert.Empty(Coverage.Complete.Reasons);
        Assert.Equal(["a", "b"], Coverage.Incomplete(["a", "b"]).Reasons);
        Assert.Equal(CoverageState.Failed, Coverage.Failed("x").State);
        Assert.Equal(CoverageState.Superseded, Coverage.Superseded("x").State);
        Assert.Equal(CoverageState.SkippedByPolicy, Coverage.SkippedByPolicy("x").State);
    }
}
