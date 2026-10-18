// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

//High performance console output - nonblocking + colour coded
//Can output to a file simultaneously
namespace LibreLancer;

public enum LogSeverity {
    Debug,
    Info,
    Warning,
    Error
}
public static class FLLog
{
#if DEBUG
		public static LogSeverity MinimumSeverity = LogSeverity.Debug;
#else
    public static LogSeverity MinimumSeverity = LogSeverity.Info;
#endif

    public static IUIThread? UIThread;
    public static Action<string,LogSeverity>? AppendLine;
    [DllImport("libc")]
    private static extern bool isatty(int desc);

    public static void Write(string component, string message, LogSeverity severity)
    {
        if ((int)severity < (int)MinimumSeverity)
            return;
        var newC = ConsoleColor.White;
        switch (severity) {
            case LogSeverity.Debug:
                newC = ConsoleColor.DarkGray;
                break;
            case LogSeverity.Error:
                newC = ConsoleColor.Red;
                break;
            case LogSeverity.Warning:
                newC = ConsoleColor.Yellow;
                break;
        }
        NonblockWrite(newC, string.Format("[{0}: {3:HH:mm:ss}] {1}: {2}", severity, component, message, DateTime.Now),severity);
    }

    private static object spewLock = new object();
    private static StreamWriter? spewFile;
    private static string? spewPath;

    /// <summary>
    /// The size in bytes at which the log file starts again. A copy of the full file then replaces the file with
    /// ".old" before its extension (see <see cref="OldSpewPath"/>). So the log of one run takes at most two times this
    /// size, also when a driver or a loop writes a line in each frame.
    /// </summary>
    public static long SpewFileLimit = 32L * 1024 * 1024;

    private struct NonblockingWrite
    {
        public LogSeverity Severity;
        public ConsoleColor Color;
        public string Value;
    }

    private static BlockingCollection<NonblockingWrite> m_Queue = new BlockingCollection<NonblockingWrite>();
    static FLLog()
    {
        Thread thread = new Thread(
            () =>
            {
                while (true)
                {
                    var q = m_Queue.Take();
                    if (UIThread != null && AppendLine != null)
                        UIThread.QueueUIThread(() => AppendLine(q.Value,q.Severity));
                    if (Platform.RunningOS == OS.Windows)
                    {
                        var c = Console.ForegroundColor;
                        Console.ForegroundColor = q.Color;
                        Console.WriteLine(q.Value);
                        Console.ForegroundColor = c;
                    }
                    else if (q.Color != ConsoleColor.White && isatty(1))
                    {
                        var cc = q.Color switch
                        {
                            ConsoleColor.DarkGray => "\x1b[90m",
                            ConsoleColor.Yellow => "\x1b[33m",
                            ConsoleColor.Red => "\x1b[91m",
                            _ => ""
                        };

                        Console.WriteLine("{0}{1}\x1b[0m", cc, q.Value);
                    }
                    else
                        Console.WriteLine(q.Value);
                    lock (spewLock)
                    {
                        if (spewFile != null)
                        {
                            spewFile.WriteLine(q.Value);
                            if (spewFile.BaseStream.Position >= SpewFileLimit)
                                RollSpewFile();
                        }
                    }
                }
            })
        {
            IsBackground = true,
            Name = "Log"
        };

        thread.Start();
    }

    private static FileStream CreateWriteLocked(string filename)
        => new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.Read);

    public static bool CreateSpewFile(string filename)
    {
        lock (spewLock)
        {
            try
            {
                spewFile?.Dispose();
                spewFile = new StreamWriter(CreateWriteLocked(filename));
                spewFile.AutoFlush = true;
                spewPath = filename;
                return true;
            }
            catch (Exception)
            {
                spewFile = null;
                spewPath = null;
                return false;
            }
        }
    }

    /// <summary>Stops the copy of the log to its file, and closes the file. Lines that the log thread did not write yet
    /// go only to the console.</summary>
    public static void CloseSpewFile()
    {
        lock (spewLock)
        {
            spewFile?.Dispose();
            spewFile = null;
            spewPath = null;
        }
    }

    /// <summary>The file that holds the older lines of a log file: ".old" before its extension, for example
    /// <c>lancer.log.old.txt</c> for <c>lancer.log.txt</c>.</summary>
    public static string OldSpewPath(string filename) =>
        Path.Combine(Path.GetDirectoryName(filename) ?? "",
            Path.GetFileNameWithoutExtension(filename) + ".old" + Path.GetExtension(filename));

    /// <summary>
    /// Copies the full log file to its old file, and starts the log file again. The log file stays open and keeps its
    /// name, so a program that shows the log can keep it open. A rename cannot do this: Windows refuses to replace a
    /// file that another program has open. The caller holds <see cref="spewLock"/>.
    /// </summary>
    private static void RollSpewFile()
    {
        var file = spewFile!;
        var old = OldSpewPath(spewPath!);
        string note;
        try
        {
            file.Flush();
            using (var source = new FileStream(spewPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var target = new FileStream(old, FileMode.Create, FileAccess.Write, FileShare.Read))
                source.CopyTo(target);
            note = $"The older lines are in {old}.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note = $"The older lines are lost, because the log could not copy them to {old}: {e.Message}";
        }
        try
        {
            file.BaseStream.SetLength(0);
            file.WriteLine(string.Format("[{0}: {1:HH:mm:ss}] Log: The log file reached {2} bytes. {3}",
                LogSeverity.Info, DateTime.Now, SpewFileLimit, note));
        }
        catch (IOException)
        {
            // The console keeps the lines.
            spewFile = null;
            try
            {
                file.Dispose();
            }
            catch (IOException)
            {
                // The file is lost already.
            }
        }
    }

    private static void NonblockWrite(ConsoleColor color, string message,LogSeverity severity)
    {
        m_Queue.Add(new NonblockingWrite() { Color = color, Value = message, Severity = severity });
    }

    public static void Flush()
    {
        while(m_Queue.Count > 0) { Thread.Sleep(1); }
    }

    public static void Info(string component, string message)
    {
        Write (component, message, LogSeverity.Info);
    }
    public static void Debug(string component, string message)
    {
        Write (component, message, LogSeverity.Debug);
    }
    public static void Warning(string component, string message)
    {
        Write (component, message, LogSeverity.Warning);
    }
    public static void Error(string component, string message)
    {
        Write (component, message, LogSeverity.Error);
    }

    public static void Exception(string component, Exception e)
    {
        var builder = new StringBuilder();
        builder.AppendLine(e.Message);
        builder.AppendLine(e.StackTrace);
        var ex = e.InnerException;
        var i = 0;

        while (ex != null)
        {
            if (i > 4)
            {
                builder.AppendLine(">>Truncated Inner Exceptions");
                break;
            }
            builder.AppendLine("Inner:");
            builder.AppendLine(ex.Message);
            builder.AppendLine(ex.StackTrace);
            ex = e.InnerException;
            i++;
        }
        Error(component, builder.ToString());
    }
}
