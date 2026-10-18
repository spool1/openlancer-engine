using System;
using System.IO;
using Xunit;

namespace LibreLancer.Tests;

// The product name of a program (OL-0036). A program that is not Librelancer gives its own name to AppHandler.Run.
// The crash and error windows show the name, and the logs and the game saves go into folders with the name.
// Run opens the log file of the process, so these tests run in the collection of the log tests.
[Collection(nameof(FLLogSpewTests))]
public class AppHandlerTests
{
    [Fact]
    public void TheLogFolderOfAProductHasItsName()
    {
        var folder = AppHandler.LogFolder("OpenLancer");
        Assert.Equal(Path.Combine("OpenLancer", "logs"),
            Path.Combine(Path.GetFileName(Path.GetDirectoryName(folder)!), Path.GetFileName(folder)));
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenLancer", "logs"), folder);
        }
    }

    [Fact]
    public void RunGivesTheProductNameToTheProgramAndToItsLog()
    {
        var name = "OpenLancerTest-" + Guid.NewGuid().ToString("N");
        var before = AppHandler.ProjectName;
        var folder = AppHandler.LogFolder(name);
        string? nameInProgram = null;
        try
        {
            AppHandler.Run(name, () => nameInProgram = AppHandler.ProjectName);

            Assert.Equal(name, nameInProgram);
            Assert.Equal(name, AppHandler.ProjectName);
            // The log file has the name of the calling assembly, also through the overload with the product name.
            Assert.True(File.Exists(Path.Combine(folder, "LibreLancer.Tests.log.txt")));
        }
        finally
        {
            FLLog.CloseSpewFile();
            // ProjectName has a private setter. Put back the name of the other tests.
            typeof(AppHandler).GetProperty(nameof(AppHandler.ProjectName))!.SetValue(null, before);
            var productFolder = Path.GetDirectoryName(folder)!;
            if (Directory.Exists(productFolder))
                Directory.Delete(productFolder, true);
        }
    }
}
