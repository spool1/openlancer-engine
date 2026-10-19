// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace LibreLancer.ImUI;

public enum ColorTextEditMode
{
    Normal,
    Lua,
    Ini
}

/// <summary>The kinds of names of the INI mode (OpenLancer).</summary>
public enum ColorTextEditIdentifiers
{
    /// <summary>Names in the keyword color, for example the actions and conditions of a mission.</summary>
    Keywords = 0,

    /// <summary>Names in the declaration color, for example the known keys of the section types.</summary>
    Declarations = 1
}
public class ColorTextEdit : IDisposable
{
    [DllImport("cimgui")]
    static extern IntPtr igExtTextEditorInit();

    [DllImport("cimgui")]
    static extern IntPtr igExtTextEditorGetText(IntPtr textedit);

    [DllImport("cimgui")]
    static extern void igExtFree(IntPtr mem);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSetText(IntPtr textedit, IntPtr text);

    [DllImport("cimgui")]
    static extern void igExtTextEditorGetCoordinates(IntPtr textedit, out int x, out int y);

    [DllImport("cimgui")]
    static extern void igExtTextEditorFree(IntPtr textedit);

    [DllImport("cimgui")]
    static extern void igExtTextEditorRender(IntPtr textedit, IntPtr id);

    [DllImport("cimgui")]
    static extern int igExtTextEditorGetUndoIndex(IntPtr textedit);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSetMode(IntPtr textedit, ColorTextEditMode mode);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSetReadOnly(IntPtr textedit, bool readOnly);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSetIdentifiers(IntPtr textedit, int kind, IntPtr names);

    [DllImport("cimgui")]
    static extern void igExtTextEditorAddMarker(IntPtr textedit, int line, uint lineColor, uint textColor,
        IntPtr lineTooltip, IntPtr textTooltip);

    [DllImport("cimgui")]
    static extern void igExtTextEditorClearMarkers(IntPtr textedit);

    [DllImport("cimgui")]
    static extern int igExtTextEditorHasMarkers(IntPtr textedit);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSetCursor(IntPtr textedit, int line, int column);

    [DllImport("cimgui")]
    static extern void igExtTextEditorScrollToLine(IntPtr textedit, int line);

    [DllImport("cimgui")]
    static extern void igExtTextEditorSelectLine(IntPtr textedit, int line);

    [DllImport("cimgui")]
    static extern int igExtTextEditorGetLineCount(IntPtr textedit);

    [DllImport("cimgui")]
    static extern int igExtTextEditorSetPalette(IntPtr textedit, uint[] colors, int count);

    /// <summary>The number of colors of a palette, in the order of the colors of the native editor: text, keyword,
    /// declaration, number, string, punctuation, preprocessor, identifier, known identifier, comment, background,
    /// cursor, selection, whitespace, the six bracket colors, line number and current line number.</summary>
    public const int PaletteSize = 22;

    /// <summary>The exports that the INI mode, the marks and the cursor calls need. An older native library does not
    /// have them.</summary>
    public static readonly string[] IniExports =
    [
        "igExtTextEditorSetIdentifiers", "igExtTextEditorAddMarker", "igExtTextEditorClearMarkers",
        "igExtTextEditorHasMarkers", "igExtTextEditorSetCursor", "igExtTextEditorScrollToLine",
        "igExtTextEditorSelectLine", "igExtTextEditorGetLineCount", "igExtTextEditorSetPalette"
    ];

    private IntPtr textedit;
    private bool textChanged = false;
    private int lastUndoIndex = 0;

    public ColorTextEdit()
    {
        textedit = igExtTextEditorInit();
    }

    public void SetText(string text)
    {
        using var ptr = UnsafeHelpers.StringToNativeUTF8(text);
        igExtTextEditorSetText(textedit, (IntPtr)ptr);
    }

    public string GetText()
    {
        var ptr = igExtTextEditorGetText(textedit);
        var str = UnsafeHelpers.PtrToStringUTF8(ptr)!;
        igExtFree(ptr);
        return str;
    }

    public void Render(string id)
    {
        ImGui.PushFont(ImGuiHelper.SystemMonospace, 17);
        using var ptr = UnsafeHelpers.StringToNativeUTF8(id);
        igExtTextEditorRender(textedit, (IntPtr)ptr);
        int idx = igExtTextEditorGetUndoIndex(textedit);
        textChanged = idx != lastUndoIndex;
        lastUndoIndex = idx;
        ImGui.PopFont();
    }

    public void SetMode(ColorTextEditMode mode)
    {
        igExtTextEditorSetMode(textedit, mode);
    }

    public void SetReadOnly(bool readOnly)
    {
        igExtTextEditorSetReadOnly(textedit, readOnly);
    }

    /// <summary>Sets the names of one kind for all editors in INI mode. They compare without letter case.</summary>
    public void SetIdentifiers(ColorTextEditIdentifiers kind, IEnumerable<string> names)
    {
        using var ptr = UnsafeHelpers.StringToNativeUTF8(string.Join('\n', names));
        igExtTextEditorSetIdentifiers(textedit, (int)kind, (IntPtr)ptr);
    }

    /// <summary>Marks a line (from 0) with a color for its number and for its text, and the tooltips of both. A new
    /// text clears all marks, so add them again after <see cref="SetText"/>.</summary>
    public void AddMarker(int line, uint lineColor, uint textColor, string lineTooltip, string textTooltip)
    {
        using var lineText = UnsafeHelpers.StringToNativeUTF8(lineTooltip);
        using var text = UnsafeHelpers.StringToNativeUTF8(textTooltip);
        igExtTextEditorAddMarker(textedit, line, lineColor, textColor, (IntPtr)lineText, (IntPtr)text);
    }

    public void ClearMarkers() => igExtTextEditorClearMarkers(textedit);

    public bool HasMarkers() => igExtTextEditorHasMarkers(textedit) != 0;

    /// <summary>Moves the cursor to a line and a column (from 0). The editor shows it at the next render.</summary>
    public void SetCursor(int line, int column) => igExtTextEditorSetCursor(textedit, line, column);

    /// <summary>Scrolls the line (from 0) to the middle of the view at the next render.</summary>
    public void ScrollToLine(int line) => igExtTextEditorScrollToLine(textedit, line);

    /// <summary>Selects a line (from 0).</summary>
    public void SelectLine(int line) => igExtTextEditorSelectLine(textedit, line);

    public int LineCount => igExtTextEditorGetLineCount(textedit);

    /// <summary>Sets the colors (ABGR, as ImGui packs them) in the order of <see cref="PaletteSize"/>. Returns false
    /// when the number of colors does not fit.</summary>
    public bool SetPalette(uint[] colors) => igExtTextEditorSetPalette(textedit, colors, colors.Length) != 0;
    public Point GetCoordinates()
    {
        igExtTextEditorGetCoordinates(textedit, out int x, out int y);
        return new Point(x,y);
    }

    public bool TextChanged()
    {
        return textChanged;
    }

    public void Dispose()
    {
        if(textedit == IntPtr.Zero)
            throw new ObjectDisposedException("ColorTextEdit");
        igExtTextEditorFree(textedit);
        textedit = IntPtr.Zero;
    }
}
