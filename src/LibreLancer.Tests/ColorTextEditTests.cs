using LibreLancer.ImUI;
using Xunit;

namespace LibreLancer.Tests;

// The INI mode, the line marks and the cursor calls of the color text editor (OL-0060). The constructor, SetText and
// AddMarker of the editor make no ImGui call, so the tests need no ImGui frame.
public class ColorTextEditTests
{
    private const string Text = "[Section]\nnickname = first ; a comment\nAct_Test = 1, -2.5, $str:name\n";

    [Fact]
    public void TheIniModeKeepsTheText()
    {
        using var editor = new ColorTextEdit();
        editor.SetMode(ColorTextEditMode.Ini);
        editor.SetIdentifiers(ColorTextEditIdentifiers.Keywords, ["Act_Test"]);
        editor.SetIdentifiers(ColorTextEditIdentifiers.Declarations, ["nickname"]);
        editor.SetText(Text);

        Assert.Equal(4, editor.LineCount);
        Assert.Equal(Text, editor.GetText());
    }

    [Fact]
    public void ANewTextClearsTheMarkers()
    {
        using var editor = new ColorTextEdit();
        editor.SetMode(ColorTextEditMode.Ini);
        editor.SetText(Text);
        editor.AddMarker(1, 0xFF0000FF, 0x400000FF, "Line 2", "A problem.");
        Assert.True(editor.HasMarkers());
        editor.ClearMarkers();
        Assert.False(editor.HasMarkers());

        editor.AddMarker(2, 0xFF00FFFF, 0x4000FFFF, "Line 3", "A warning.");
        Assert.True(editor.HasMarkers());
        editor.SetText(Text);
        Assert.False(editor.HasMarkers());
        Assert.Equal(Text, editor.GetText());
    }

    [Fact]
    public void AMarkerOutsideTheTextIsIgnored()
    {
        using var editor = new ColorTextEdit();
        editor.SetText("one line");
        editor.AddMarker(5, 0xFF0000FF, 0x400000FF, "", "");
        editor.AddMarker(-1, 0xFF0000FF, 0x400000FF, "", "");
        Assert.False(editor.HasMarkers());
    }

    [Fact]
    public void TheCursorCallsAndThePaletteNeedNoFrame()
    {
        using var editor = new ColorTextEdit();
        editor.SetMode(ColorTextEditMode.Ini);
        editor.SetText(Text);
        editor.SetCursor(2, 3);
        editor.SelectLine(1);
        editor.ScrollToLine(2);

        Assert.True(editor.SetPalette(new uint[ColorTextEdit.PaletteSize]));
        Assert.False(editor.SetPalette(new uint[3]));
        Assert.Equal(Text, editor.GetText());
    }
}
