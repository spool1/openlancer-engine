using System.Runtime.CompilerServices;

namespace LibreLancer.Tests;

// On Windows, the build puts the native libraries in the x64 folder next to the test assembly. The game and the
// editors add that folder to the DLL search path when they start (AppHandler). The tests must do the same, else
// each test that calls thorncompiler.dll fails with DllNotFoundException.
internal static class NativeLibraries
{
    [ModuleInitializer]
    internal static void AddSearchPath() => AppHandler.ConsoleInit(silent: true);
}
