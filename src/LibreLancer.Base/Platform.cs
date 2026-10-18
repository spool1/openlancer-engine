// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibreLancer.Platforms;

namespace LibreLancer;

internal abstract class PlatformEvents : IDisposable
{
    public virtual void WndProc(uint msg, UIntPtr wParam)
    {

    }

    public virtual void Poll()
    {
    }

    public abstract void Dispose();
}

public record MountInfo(string Name, string Path);

public static class Platform
{
    public static OS RunningOS;
    internal static IPlatform RunningPlatform;
    public static string OSDescription;

    public static event Action<MountInfo[]>? MountsChanged;

    internal static void OnMountsChanged(MountInfo[] mounts)
    {
        MountsChanged?.Invoke(mounts);
    }

    private static readonly Dictionary<string, string> hintValues = new(StringComparer.OrdinalIgnoreCase);

    public static string GetHintString(string hint, string def) => hintValues.GetValueOrDefault(hint, def);

    public static bool GetHintBoolean(string hint, bool def)
    {
        if (hintValues.TryGetValue(hint, out var v))
        {
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return def;
    }

    static Platform ()
    {
        OSDescription = $"{RuntimeInformation.OSDescription} - {RuntimeInformation.ProcessArchitecture}";
        switch (Environment.OSVersion.Platform) {
            case PlatformID.Unix:
                if (Directory.Exists ("/Applications")
                    & Directory.Exists ("/System")
                    & Directory.Exists ("/Users")
                    & Directory.Exists ("/Volumes")) {
                    RunningOS = OS.Mac;
                    RunningPlatform = new MacOSPlatform();
                } else {
                    RunningOS = OS.Linux;
                    RunningPlatform = new LinuxPlatform ();
                    //Get current distribution
                    if (File.Exists("/etc/os-release"))
                    {
                        var x = Regex.Match(File.ReadAllText("/etc/os-release"), @"^PRETTY_NAME\s*?\=(.*)$",
                            RegexOptions.Multiline);
                        if (x.Success && x.Groups[1].Length > 0)
                            OSDescription = x.Groups[1].Value.TrimStart('\"', '\'').TrimEnd('\"', '\'') + " " +
                                            OSDescription;
                    }
                    else if (Shell.HasCommand("lsb_release")) {
                        var lsbVersion = Shell.GetString("lsb_release", "-d");
                        if (!string.IsNullOrWhiteSpace(lsbVersion))
                            OSDescription = lsbVersion + " " + OSDescription;
                    }
                }
                break;
            case PlatformID.MacOSX:
                RunningOS = OS.Mac;
                RunningPlatform = new MacOSPlatform();
                break;
            default:
                RunningOS = OS.Windows;
                RunningPlatform = new Win32Platform ();
                break;
        }
        RegisterDllMap(typeof(Platform).Assembly);
        var env = Environment.GetEnvironmentVariables();
        foreach (object key in env.Keys)
        {
            var k = key.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(k))
            {
                string value = env[key]?.ToString() ?? "";
                hintValues[k] = value;
            }
        }
    }

    internal static void Init(string sdlBackend) => RunningPlatform.Init(sdlBackend);

    internal static PlatformEvents SubscribeEvents(IUIThread mainThread) =>
        RunningPlatform.SubscribeEvents(mainThread);

    public static MountInfo[] GetMounts() => RunningPlatform.GetMounts();

    public static string GetBasePath()
    {
        using var processModule = Process.GetCurrentProcess().MainModule;
        var basePath = Path.GetDirectoryName(processModule?.FileName) ?? AppDomain.CurrentDomain.BaseDirectory;
        return basePath;
    }

    private static bool? _portable;
    public static bool IsPortable()
    {
        if (_portable.HasValue)
        {
            return _portable.Value;
        }

        _portable =  File.Exists(Path.Combine(Path.GetDirectoryName(typeof(Platform).Assembly.Location) ?? string.Empty, "portable"));
        if (_portable.Value)
        {
            FLLog.Info("Data", "Running in portable mode");
        }

        return _portable.Value;
    }

    public static string GetLocalConfigFolder() =>
        IsPortable() ? GetBasePath() : RunningPlatform.GetLocalConfigFolder();

    public static bool IsDirCaseSensitive (string directory)
    {
        return RunningPlatform.IsDirCaseSensitive (directory);
    }

    public static void RegisterDllMap(Assembly assembly)
    {
        if(RunningOS is OS.Linux or OS.Mac)
            DllMap.Register(assembly);
    }
    public static byte[]? GetMonospaceBytes()
    {
        return RunningPlatform.GetMonospaceBytes();
    }

    internal static void Shutdown()
    {
        RunningPlatform.Shutdown();
    }

    public static string? GetInformationalVersion<T>()
    {
        return ((AssemblyInformationalVersionAttribute)Assembly
            .GetAssembly(typeof(T))
            ?.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)[0]!)?.InformationalVersion;
    }
}

public enum OS
{
    Windows,
    Mac,
    Linux
}
