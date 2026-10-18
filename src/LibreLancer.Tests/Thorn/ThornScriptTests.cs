using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibreLancer.Thorn.VM;
using Xunit;

namespace LibreLancer.Tests.Thorn;

public class ThornScriptTests
{
    public static IEnumerable<object[]> GetTestCases()
    {
        return Directory.GetFiles("Thorn/Scripts", "*.lua", SearchOption.AllDirectories)
            .Select(x => new object[] { x });
    }

    [Theory]
    [MemberData(nameof(GetTestCases))]
    public void RunScript(string test)
    {
        var output = Path.ChangeExtension(test, ".txt");
        if (!File.Exists(output)) {
            throw new Exception($"Output file missing {output}");
        }

        // A Windows checkout can give the expected file CRLF line ends, and the scripts write LF. Compare the text only.
        var expected = File.ReadAllText(output).ReplaceLineEndings("\n").Trim();
        var script = File.ReadAllText(test);

        var builder = new StringBuilder();

        var runtime = new ThornRuntime();
        runtime.SetBuiltins();
        runtime.OnStdout += e => builder.Append(e);
        runtime.DoString(script, test);

        Assert.Equal(expected, builder.ToString().ReplaceLineEndings("\n").Trim());
    }
}
