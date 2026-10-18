using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibreLancer.Dialogs;

namespace LibreLancer;

public static class AppHandler
{
    [DllImport("kernel32.dll")]
    private static extern bool SetDllDirectory(string directory);

    /// <summary>
    /// The name of the product. The crash and error windows show it, and the folders of the logs and of the game saves
    /// use it. <see cref="Run(string, Action, Action?)"/> sets it. A program that calls
    /// <see cref="Run(Action, Action?)"/> keeps "Librelancer".
    /// </summary>
    public static string ProjectName { get; private set; } = "Librelancer";

    /// <summary>The folder of the log files of a product, for example <c>%LOCALAPPDATA%\Librelancer\logs</c> on
    /// Windows.</summary>
    public static string LogFolder(string projectName) => Path.Combine(LogsFolder(), projectName, "logs");

    private static void LogPlatform()
    {
        FLLog.Info("Platform", Platform.OSDescription);
        FLLog.Info("Available Threads", Environment.ProcessorCount.ToString());
    }

    public static void ConsoleInit(bool silent = false)
    {
        if (!silent)
        {
            LogPlatform();
        }

        if (Platform.RunningOS != OS.Windows)
        {
            return;
        }

        var binDir = Path.GetDirectoryName(Assembly.GetCallingAssembly().Location) ?? "";
        var fullPath = Path.Combine(binDir, IntPtr.Size == 8 ? "x64" : "x86");
        SetDllDirectory(fullPath);
    }

    private static string LogsFolder()
    {
        if (Platform.RunningOS != OS.Linux)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        var statePath = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!string.IsNullOrEmpty(statePath))
        {
            return statePath;
        }

        statePath = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(statePath))
        {
            return "./data";
        }

        return statePath + "/.local/state";
    }

    private static bool FileOk(string file)
    {
        if (!File.Exists(file))
            return true;
        try
        {
            var stream = File.OpenWrite(file);
            stream.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // NoInlining keeps the program assembly as the calling assembly. It names the log file and finds the natives.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(Action action, Action? onCrash = null) =>
        RunProgram(ProjectName, Assembly.GetCallingAssembly(), action, onCrash);

    /// <summary>
    /// Runs a program with a product name of its own, for example a program that is not Librelancer. The name goes into
    /// <see cref="ProjectName"/> before the program starts.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string projectName, Action action, Action? onCrash = null) =>
        RunProgram(projectName, Assembly.GetCallingAssembly(), action, onCrash);

    private static void RunProgram(string projectName, Assembly program, Action action, Action? onCrash)
    {
        ProjectName = projectName;
        var errorMessage = $"{ProjectName} has crashed. See the log for more information.";

        if (Platform.RunningOS == OS.Windows)
        {
            var binDir = Path.GetDirectoryName(program.Location) ?? "";
            var fullPath = Path.Combine(binDir, IntPtr.Size == 8 ? "x64" : "x86");
            SetDllDirectory(fullPath);
        }

        //Setup Spew
        var spewFolder = LogFolder(ProjectName);
        if (!Directory.Exists(spewFolder))
        {
            Directory.CreateDirectory(spewFolder);
        }

        var spewBase = program.GetName().Name + ".log";
        var spewSuffix = ".txt";

        string? spewPath = null;
        var fileCounter = 0;
        do
        {
            var tryPath = Path.Combine(spewFolder, spewBase + spewSuffix);
            if (FileOk(tryPath))
            {
                spewPath = tryPath;
                break;
            }

            if (fileCounter > 5)
            {
                break;
            }

            spewSuffix = $".{fileCounter++}.txt";
        } while (true);

        if (spewPath != null && FLLog.CreateSpewFile(spewPath))
        {
            errorMessage += "\n" + spewPath;
        }
        else
        {
            errorMessage += "\n(Log file could not be created).";
            spewPath = null;
        }

        LogPlatform();
        if (spewPath != null)
        {
            FLLog.Info("Log Path", spewPath);
        }

#if !DEBUG
        var domain = AppDomain.CurrentDomain;
        domain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) => {
            var ex = (Exception)(e.ExceptionObject);

            CrashWindow.Run("Uh-oh!", errorMessage,
                FormatException(ex).ToString());
        };

        try
        {
#endif
            action();
#if !DEBUG
        }
        catch (Exception ex)
        {
            try { onCrash?.Invoke(); }
            catch
            {
                // ignored
            }

            CrashWindow.Run("Uh-oh!", errorMessage, FormatException(ex).ToString());
        }

#endif
    }

    private static StringBuilder FormatException(Exception ex, StringBuilder? builder = null, int j = 0)
    {
        var addVersion = j == 0;
        builder ??= new StringBuilder();
        builder.AppendLine(ex.Message);
        builder.AppendLine(ex.StackTrace);
        j++;
        if (j > 100)
        {
            builder.AppendLine("-- EXCEPTION OVERFLOW --");
            return builder;
        }

        if (ex is AggregateException ag)
        {
            for (var i = 0; i < ag.InnerExceptions.Count; i++)
            {
                builder.AppendLine($"Inner Exception #{i + 1}:");
                FormatException(ag.InnerExceptions[i], builder, j);
            }
        }
        else
        {
            if (ex.InnerException != null)
            {
                builder.AppendLine("Inner Exception: ");
                FormatException(ex.InnerException, builder, j);
            }
        }

        if (addVersion)
        {
            builder.AppendLine()
                .AppendLine($"{ProjectName} Version: {Platform.GetInformationalVersion<Platforms.IPlatform>()}");
        }

        return builder;
    }
}
