using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace LibreLancer.Tests;

// The size limit of the log file (OL-0033). A driver or a loop that logs a line in each frame filled the log of a
// three-hour editor run to 1.1 GB. Now the log file starts again at the limit, and the older lines replace the old
// file. So the log of a run takes at most two times the limit. The log is one static object, so the test runs alone:
// a line of another test would go into the test file.
[CollectionDefinition(nameof(FLLogSpewTests), DisableParallelization = true)]
public class FLLogSpewCollection;

[Collection(nameof(FLLogSpewTests))]
public class FLLogSpewTests
{
    [Fact]
    public void TheLogFileStartsAgainAtItsLimitAndKeepsOneOldFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ol-fllog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test.log.txt");
        var old = Path.Combine(folder, "test.log.old.txt");
        var limit = FLLog.SpewFileLimit;
        try
        {
            Assert.Equal(old, FLLog.OldSpewPath(path));
            FLLog.SpewFileLimit = 2000;
            Assert.True(FLLog.CreateSpewFile(path));
            const int count = 100;
            for (var i = 0; i < count; i++)
                FLLog.Info("SpewTest", $"line {i:D3} " + new string('x', 40));
            WaitFor(() => File.Exists(path) && Read(path).Any(l => l.Contains("SpewTest: line 099")));

            var current = Read(path);
            var older = Read(old);
            // Each file ends at the first line that reaches the limit, so only its last line can pass the limit.
            Assert.True(new FileInfo(old).Length - older[^1].Length - Environment.NewLine.Length < 2000);
            Assert.True(new FileInfo(path).Length - current[^1].Length - Environment.NewLine.Length < 2000);
            // The new file starts with a note that names the old file.
            Assert.Equal($"Log: The log file reached 2000 bytes. The older lines are in {old}.",
                current[0][(current[0].IndexOf("] ", StringComparison.Ordinal) + 2)..]);
            // The two files hold the last lines in order, without a gap.
            var numbers = older.Concat(current)
                .Where(l => l.Contains("SpewTest: line "))
                .Select(l => int.Parse(l.Substring(l.IndexOf("SpewTest: line ", StringComparison.Ordinal) + 15, 3)))
                .ToList();
            Assert.Equal(count - 1, numbers[^1]);
            Assert.Equal(Enumerable.Range(numbers[0], numbers.Count), numbers);
            Assert.True(numbers[0] > 0, "The limit must start the file again more than once.");
        }
        finally
        {
            FLLog.CloseSpewFile();
            FLLog.SpewFileLimit = limit;
            Directory.Delete(folder, true);
        }
    }

    private static string[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void WaitFor(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), "The log thread did not write the lines.");
            Thread.Sleep(10);
        }
    }
}
