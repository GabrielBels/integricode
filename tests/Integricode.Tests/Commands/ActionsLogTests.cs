using Integricode.Commands;

namespace Integricode.Tests.Commands;

public class ActionsLogTests
{
    [Fact]
    public void Each_level_writes_its_workflow_command()
    {
        var writer = new StringWriter();
        var log = new ActionsLog(writer);

        log.Info("plain");
        log.Notice("note");
        log.Warning("careful");
        log.Error("broken");

        Assert.Equal(["plain", "::notice::note", "::warning::careful", "::error::broken"], writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Percent_signs_and_line_breaks_are_escaped_so_a_message_cannot_start_another_command()
    {
        var writer = new StringWriter();

        new ActionsLog(writer).Warning("50% done\r\n::error::injected\nnext");

        Assert.Equal("::warning::50%25 done%0D%0A::error::injected%0Anext" + Environment.NewLine, writer.ToString());
    }
}
