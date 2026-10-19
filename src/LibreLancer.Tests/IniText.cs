using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibreLancer.Data.Ini;
using Xunit;

namespace LibreLancer.Tests;

// Parses INI text of a test into sections, as the engine reads a text INI file of the game data.
internal static class IniText
{
    public const string File = "test.ini";

    public static List<Section> Sections(string text) =>
        IniFile.ParseFile(File, new MemoryStream(Encoding.UTF8.GetBytes(text))).ToList();

    public static Section Section(string text) => Assert.Single(Sections(text));
}
