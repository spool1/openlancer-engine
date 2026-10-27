using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LibreLancer.Interface;
using WattleScript.Interpreter;
using Xunit;

namespace LibreLancer.Tests;

// The scripts of the default interface and the rectangle of a display element (OL-0072). X, Y, Width and Height of a
// display element are a Metric. A script that sets one of them to a plain number stops with an error, so the HUD did not
// open while the map listed a waypoint.
public class InterfaceScriptTests
{
    private static InterfaceTextBundle DefaultInterface()
    {
        using var reader = new StreamReader(typeof(UiContext).Assembly
            .GetManifestResourceStream("LibreLancer.Interface.Default.interface.json")!);
        return InterfaceTextBundle.FromJSON(reader.ReadToEnd());
    }

    [Fact]
    public void AScriptSetsTheThicknessOfAWireBorderButNotItsWidthWithANumber()
    {
        LuaContext.Initialize();
        var wire = new DisplayWireBorder();
        var script = new Script(CoreModules.Preset_SoftSandboxWattle) { Options = { Syntax = ScriptSyntax.Wattle } };
        script.Globals["wire"] = wire;

        script.DoString("wire.Thickness = 2");

        Assert.Equal(2, wire.Thickness);
        Assert.Throws<ScriptRuntimeException>(() => script.DoString("wire.Width = 1"));
    }

    [Fact]
    public void NoScriptOfTheDefaultInterfaceSetsTheRectangleOfADisplayElementToANumber()
    {
        var bundle = DefaultInterface();
        var scripts = bundle.db.Keys.Where(k => k.EndsWith(".lua")).ToList();
        var made = new Regex(@"(\w+)\s*=\s*NewObject\(\s*[""']Display\w+[""']\s*\)");

        foreach (var key in scripts)
        {
            var text = bundle.GetStringCompressed(key);
            foreach (var name in made.Matches(text).Select(m => m.Groups[1].Value).Distinct())
            {
                Assert.DoesNotMatch(new Regex($@"\b{name}\.(X|Y|Width|Height)\s*=\s*-?[\d.]"), text);
            }
        }

        Assert.Contains("mapwindow.lua", scripts);
    }
}
