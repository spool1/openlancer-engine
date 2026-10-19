// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

#include "cimgui_ext.h"
#include "imgui.h"
#include "TextEditor.h"
#include <string>
#include <cstring>
#include <string_view>

// The INI mode of OpenLancer: one language object that lives as long as the program, because the editor keeps a
// pointer to its language. A ';' starts a comment. Section headers, numbers, '=' and ',' and '$str:' placeholders get
// their own colors. The names compare without letter case. igExtTextEditorSetIdentifiers fills the keyword set (the
// action and condition names) and the declaration set (the known keys) of all editors in INI mode.

static bool IsIniWordStart(ImWchar c)
{
    return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
}

static bool IsIniWordPart(ImWchar c)
{
    return IsIniWordStart(c) || (c >= '0' && c <= '9');
}

static TextEditor::Iterator GetIniIdentifier(TextEditor::Iterator start, TextEditor::Iterator end)
{
    if (start < end && IsIniWordStart(*start)) {
        start++;
        while (start < end && IsIniWordPart(*start))
            start++;
    }
    return start;
}

// A number: an optional sign, digits with an optional fraction, and an optional exponent. A number that a letter or a
// '_' follows is a part of a name, so it is no number.
static TextEditor::Iterator GetIniNumber(TextEditor::Iterator start, TextEditor::Iterator end)
{
    auto i = start;
    if (i < end && (*i == '-' || *i == '+'))
        i++;
    auto digits = i;
    while (i < end && *i >= '0' && *i <= '9')
        i++;
    if (i < end && *i == '.') {
        i++;
        while (i < end && *i >= '0' && *i <= '9')
            i++;
    }
    if (i == digits || (i - digits == 1 && *digits == '.'))
        return start;
    if (i < end && (*i == 'e' || *i == 'E')) {
        auto exponent = i;
        i++;
        if (i < end && (*i == '-' || *i == '+'))
            i++;
        auto first = i;
        while (i < end && *i >= '0' && *i <= '9')
            i++;
        if (i == first)
            i = exponent;
    }
    if (i < end && IsIniWordPart(*i))
        return start;
    return i;
}

static bool IsIniPunctuation(ImWchar c)
{
    return c == '=' || c == ',';
}

// Section headers and '$str:' placeholders.
static TextEditor::Iterator GetIniToken(TextEditor::Iterator start, TextEditor::Iterator end, TextEditor::Color& color)
{
    if (start < end && *start == '[') {
        auto i = start;
        while (i < end && *i != ']')
            i++;
        if (i < end) {
            color = TextEditor::Color::preprocessor;
            i++;
            return i;
        }
        return start;
    }
    static const char placeholder[] = "$str:";
    auto i = start;
    for (int k = 0; placeholder[k] != 0; k++) {
        if (!(i < end) || *i != (ImWchar)placeholder[k])
            return start;
        i++;
    }
    while (i < end && IsIniWordPart(*i))
        i++;
    color = TextEditor::Color::string;
    return i;
}

static TextEditor::Language* IniLanguage()
{
    static bool initialized = false;
    static TextEditor::Language language;
    if (!initialized) {
        language.name = "INI";
        language.caseSensitive = false;
        language.singleLineComment = ";";
        language.isPunctuation = IsIniPunctuation;
        language.getIdentifier = GetIniIdentifier;
        language.getNumber = GetIniNumber;
        language.customTokenizer = GetIniToken;
        initialized = true;
    }
    return &language;
}




CIMGUI_API texteditor_t igExtTextEditorInit()
{
	TextEditor *editor = new TextEditor();
    editor->SetShowWhitespacesEnabled(false);
    editor->SetCompletePairedGlyphs(false);
    editor->SetTabSize(4);
    editor->SetInsertSpacesOnTabs(true);
	return (texteditor_t)editor;
}

CIMGUI_API void igExtTextEditorSetMode(texteditor_t textedit, texteditor_mode_t mode)
{
    TextEditor *editor = (TextEditor*)textedit;
    if(mode == TEXTEDITOR_MODE_LUA) {
        editor->SetLanguage(TextEditor::Language::WattleScript());
    }
    else if(mode == TEXTEDITOR_MODE_INI) {
        editor->SetLanguage(IniLanguage());
    }
    else {
        editor->SetLanguage(nullptr);
    }
}
CIMGUI_API void igExtTextEditorSetReadOnly(texteditor_t textedit, int readonly)
{
    TextEditor *editor = (TextEditor*)textedit;
    editor->SetReadOnlyEnabled(readonly != 0 ? true : false);
}

CIMGUI_API const char *igExtTextEditorGetText(texteditor_t textedit)
{
	TextEditor *editor = (TextEditor*)textedit;
	return strdup(editor->GetText().c_str());
}

CIMGUI_API void igExtFree(void *mem)
{
	free(mem);
}

CIMGUI_API void igExtTextEditorSetText(texteditor_t textedit, const char *text)
{
	TextEditor *editor = (TextEditor*)textedit;
	editor->SetText(text);
}

CIMGUI_API int igExtTextEditorGetUndoIndex(texteditor_t textedit)
{
	TextEditor *editor = (TextEditor*)textedit;
	return editor->GetUndoIndex();
}

CIMGUI_API void igExtTextEditorGetCoordinates(texteditor_t textedit, int32_t *x, int32_t *y)
{
	TextEditor *editor = (TextEditor*)textedit;
	auto cursor = editor->GetCursorPosition(0);
	*x = cursor.column;
	*y = cursor.line;
}

CIMGUI_API void igExtTextEditorRender(texteditor_t textedit, const char *id)
{
	TextEditor *editor = (TextEditor*)textedit;
	editor->Render(id, ImVec2(0,0), false);
}

CIMGUI_API void igExtTextEditorFree(texteditor_t textedit)
{
	delete ((TextEditor*)textedit);
}

// Sets the names of one kind for all editors in INI mode: kind 0 the keywords, kind 1 the declarations. The names are
// separated by '\n'. The editor colors its text again.
CIMGUI_API void igExtTextEditorSetIdentifiers(texteditor_t textedit, int kind, const char *names)
{
    TextEditor *editor = (TextEditor*)textedit;
    auto language = IniLanguage();
    auto& set = kind == 0 ? language->keywords : language->declarations;
    set.clear();
    std::string_view text(names ? names : "");
    size_t at = 0;
    while (at <= text.size()) {
        auto next = text.find('\n', at);
        if (next == std::string_view::npos)
            next = text.size();
        std::string name(text.substr(at, next - at));
        for (auto& c : name) {
            if (c >= 'A' && c <= 'Z')
                c = (char)(c - 'A' + 'a');
        }
        if (!name.empty())
            set.insert(name);
        at = next + 1;
    }
    if (editor->GetLanguage() == language)
        editor->SetLanguage(language);
}

// Marks a line (zero-based). TextEditor::SetText clears all markers, so the caller adds the marks again after a new text.
CIMGUI_API void igExtTextEditorAddMarker(texteditor_t textedit, int line, uint32_t lineColor, uint32_t textColor, const char *lineTooltip, const char *textTooltip)
{
    TextEditor *editor = (TextEditor*)textedit;
    if (line < 0 || line >= editor->GetLineCount())
        return;
    editor->AddMarker(line, lineColor, textColor, lineTooltip ? lineTooltip : "", textTooltip ? textTooltip : "");
}

CIMGUI_API void igExtTextEditorClearMarkers(texteditor_t textedit)
{
    ((TextEditor*)textedit)->ClearMarkers();
}

CIMGUI_API int igExtTextEditorHasMarkers(texteditor_t textedit)
{
    return ((TextEditor*)textedit)->HasMarkers() ? 1 : 0;
}

CIMGUI_API void igExtTextEditorSetCursor(texteditor_t textedit, int line, int column)
{
    ((TextEditor*)textedit)->SetCursor(line, column);
}

CIMGUI_API void igExtTextEditorScrollToLine(texteditor_t textedit, int line)
{
    ((TextEditor*)textedit)->ScrollToLine(line, TextEditor::Scroll::alignMiddle);
}

CIMGUI_API void igExtTextEditorSelectLine(texteditor_t textedit, int line)
{
    ((TextEditor*)textedit)->SelectLine(line);
}

CIMGUI_API int igExtTextEditorGetLineCount(texteditor_t textedit)
{
    return ((TextEditor*)textedit)->GetLineCount();
}

// Sets the colors of the editor, in the order of TextEditor::Color. Returns 0 when the count does not fit.
CIMGUI_API int igExtTextEditorSetPalette(texteditor_t textedit, const uint32_t *colors, int count)
{
    TextEditor::Palette palette;
    if (count != (int)palette.size())
        return 0;
    for (int i = 0; i < count; i++)
        palette[i] = colors[i];
    ((TextEditor*)textedit)->SetPalette(palette);
    return 1;
}


