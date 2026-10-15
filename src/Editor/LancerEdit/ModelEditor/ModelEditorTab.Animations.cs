using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using LibreLancer;
using LibreLancer.ContentEdit;
using LibreLancer.ContentEdit.Model;
using LibreLancer.Dialogs;
using LibreLancer.ImUI;
using LibreLancer.Utf;
using LibreLancer.Utf.Anm;
using LibreLancer.Utf.Cmp;

namespace LancerEdit.ModelEditor;

partial class ModelEditorTab
{
    enum AnimEditAction { None, Play, Backwards, Rename, Delete }
    bool AnimationHeader(string name, float playTime, out AnimEditAction act)
    {
        // Draw play percent
        if (playTime > 0f)
        {
            var cpos = ImGui.GetCursorScreenPos();
            var fH = ImGui.GetFrameHeight();
            var width = ImGui.GetContentRegionAvail().X;
            var dList = ImGui.GetWindowDrawList();
            dList.AddRectFilled(cpos, cpos + new Vector2(width * playTime, fH), (VertexDiffuse)Color4.DarkOrange,
                ImGui.GetStyle().FrameRounding, ImDrawFlags.RoundCornersAll);
        }
        // Do header
        act = AnimEditAction.None;
        ImGui.PushStyleColor(ImGuiCol.Header, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0,0));
        ImGui.PushID(name);
        var isOpen = ImGui.CollapsingHeader("##Header", ImGuiTreeNodeFlags.AllowOverlap);
        ImGui.SameLine();
        if (ImGui.Button($"{Icons.Play}")) act = AnimEditAction.Play;
        ImGui.SetItemTooltip("Play");
        ImGui.SameLine();
        if (ImGui.Button($"{Icons.Backward}")) act = AnimEditAction.Backwards;
        ImGui.SetItemTooltip("Reverse");
        ImGui.PopStyleVar();
        ImGui.SameLine();
        ImGui.Text(name);
        if (ImGui.BeginPopupContextItem("animcontext"))
        {
            if (Theme.IconMenuItem(Icons.Edit, "Rename", true)) act = AnimEditAction.Rename;
            if (Theme.IconMenuItem(Icons.TrashAlt, "Delete", true)) act = AnimEditAction.Delete;
            ImGui.Separator();
            if (Theme.IconMenuItem(Icons.Copy, "Copy Nickname", true)) _window.SetClipboardText(name);
            ImGui.EndPopup();
        }
        ImGui.PopID();
        ImGui.PopStyleColor();
        return isOpen;
    }

    IEnumerable<AbstractConstruct> IterateConstructs()
    {
        foreach (var p in vmsModel.AllParts)
        {
            if (p.Construct is { } ac)
                yield return ac;
        }
    }

    // Symbols except ,
    static char? AnimNameFilter(char ch)
    {
        if (ch == ',')
            return null;
        if (char.IsAscii(ch))
        {
            return ch;
        }
        return null;
    }

    public void ResetAnimationPlayback()
    {
        animator.ResetAnimations();
    }

    class AddJointMap(Script target, JointMap jointMap, ModelEditorTab parent) : EditorAction
    {
        public override void Commit()
        {
            target.JointMaps.Add(jointMap);
        }

        public override void Undo()
        {
            target.JointMaps.RemoveAt(target.JointMaps.Count - 1);
            parent.ResetAnimationPlayback();
        }

        public override string ToString() => $"Add Joint Map '{jointMap.ChildName}'";
    }

    class AddScript(AnmFile anm, Script sc, ModelEditorTab parent) : EditorAction
    {
        public override void Commit()
        {
            anm.Scripts.Add(sc.Name, sc);
        }

        public override void Undo()
        {
            anm.Scripts.Remove(sc.Name);
            parent.ResetAnimationPlayback();
        }

        public override string ToString() => $"Add Script '{sc.Name}'";
    }

    class RemoveJointMap(Script sc, int index, JointMap jointMap, ModelEditorTab parent) : EditorAction
    {
        public override void Commit()
        {
            sc.JointMaps.RemoveAt(index);
            parent.ResetAnimationPlayback();
        }

        public override void Undo()
        {
            sc.JointMaps.Insert(index, jointMap);
            parent.ResetAnimationPlayback();
        }

        public override string ToString() => $"Remove Joint Map '{jointMap.ChildName}'";
    }

    class RemoveScript(AnmFile anm, Script sc, ModelEditorTab parent) : EditorAction
    {
        public override void Commit()
        {
            anm.Scripts.Remove(sc.Name);
            parent.ResetAnimationPlayback();
        }

        public override void Undo()
        {
            anm.Scripts.Add(sc.Name, sc);
        }

        public override string ToString() => $"Remove Script '{sc.Name}'";
    }

    class RenameScript(Script target, AnmFile anm, string oldValue, string newValue)
        : EditorModification<string>(oldValue, newValue)
    {
        public override void Set(string value)
        {
            anm.Scripts.Remove(target.Name);
            target.Name = value;
            anm.Scripts[target.Name] = target;
        }

        public override string ToString() => $"Rename Script '{oldValue}'->'{newValue}";
    }

    void AnimationPanel()
    {
        var anm = ((CmpFile)drawable).Animation;
        if (ImGui.Button($"{Icons.PlusCircle} New"))
        {
            var c = NameInputConfig.Nickname("New Animation", x => anm.Scripts.ContainsKey(x.Trim()));
            c.Filter = AnimNameFilter;
            popups.OpenPopup(new NameInputPopup(c, "", newName =>
            {
                newName = newName.Trim();
                undoBuffer.Commit(new AddScript(anm, new Script(newName), this));
            }));
        }
        if (ImGui.Button($"{Icons.FileImport} Add From File"))
        {
            FileDialog.Open(x =>
            {
                if (x != null)
                {
                    try
                    {
                        using var stream = File.OpenRead(x);
                        var anmFile = new AnmFile(x, stream);
                        if (anmFile.Scripts.Count == 0)
                        {
                            popups.MessageBox("Empty File", "File contains no animation data.");
                            return;
                        }
                        anmFile.Buffer.Commit();

                        popups.OpenPopup(new AnimationImportPopup(anmFile, IterateConstructs().ToArray(), scripts =>
                        {
                            List<EditorAction> adds = new();
                            foreach (var sc in scripts)
                            {
                                string n = sc.Name;
                                int i = 1;
                                while (anm.Scripts.ContainsKey(n))
                                {
                                    n = $"{sc.Name}.{i++}";
                                }
                                sc.Name = n;
                                adds.Add(new AddScript(anm, sc, this));
                            }
                            if(adds.Count > 0)
                                undoBuffer.Commit(EditorAggregateAction.Create(adds.ToArray()));
                        }));
                    }
                    catch (Exception e)
                    {
                        popups.MessageBox("Error", e.ToString());
                    }
                }
            }, AppFilters.UtfFilters);
        }
        ImGui.SameLine();
        if (ImGuiExt.Button($"{Icons.FileExport} Save .anm", (anm?.Scripts.Count ?? 0) > 0))
        {
            FileDialog.Save(x =>
            {
                if (x != null)
                {
                    var output = new EditableUtf();
                    var destNode = new LUtfNode() { Name = "Animation", Parent = output.Root };
                    output.Root.Children!.Add(destNode);
                    AnimationWriter.WriteAnimations(destNode, anm!);
                    var res = output.Save(x);
                    _window.ResultMessages(res);
                }
            }, AppFilters.AnmFilters);
        }
        ImGui.Separator();
        if (ImGui.Button($"{Icons.SyncAlt} Reset Playback")) ResetAnimationPlayback();

        int uniqueid = 0;
        foreach (var sc in anm.Scripts)
        {
            ImGui.PushID(uniqueid++);
            if (AnimationHeader(sc.Key, animator.GetPlayPosition(sc.Value),out var act))
            {
                ImGui.BeginGroup();
                if (ImGui.Button("Add Joint Map"))
                {
                    popups.OpenPopup(new AddJointMapPopup(IterateConstructs(), sc.Value, con =>
                    {
                        int type = 0x1;
                        if (con is SphereConstruct) type = 0x4;
                        if (con is LooseConstruct) type = 0x2 | 0x4;
                        var jm = new JointMap();
                        jm.ParentName = con.ParentName;
                        jm.ChildName = con.ChildName;
                        jm.Channel = new(type, 0, -1, new EditableAnmBuffer(64));
                        undoBuffer.Commit(new AddJointMap(sc.Value, jm, this));
                    }));
                }
                for (int i = 0; i < sc.Value.JointMaps.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0,0));
                    if (ImGui.Button($"{Icons.Edit}"))
                    {
                        popups.OpenPopup(new JointMapEditor(sc.Value.JointMaps, i, sc.Value.Name, undoBuffer, this));
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"{Icons.TrashAlt}"))
                    {
                        int index = i;
                        _window.Confirm($"Are you sure you want to delete {sc.Value.JointMaps[i].ChildName}?", () =>
                        {
                            undoBuffer.Commit(new RemoveJointMap(sc.Value, index, sc.Value.JointMaps[index], this));
                            sc.Value.JointMaps.RemoveAt(index);
                        });
                    }
                    ImGui.SameLine();
                    ImGui.PopStyleVar();
                    ImGui.Text(sc.Value.JointMaps[i].ChildName);
                    ImGui.PopID();
                }
                ImGui.EndGroup();
            }

            switch (act)
            {
                case AnimEditAction.Play:
                    animator.StartAnimation(sc.Value, false);
                    break;
                case AnimEditAction.Backwards:
                    animator.StartAnimation(sc.Value, false, 0, 1, 0, true);
                    break;
                case AnimEditAction.Delete:
                    _window.Confirm($"Are you sure you want to delete {sc.Key}?", () =>
                    {
                        undoBuffer.Commit(new RemoveScript(anm, sc.Value, this));
                    });
                    break;
                case AnimEditAction.Rename:
                {
                    var c = NameInputConfig.Rename();
                    c.InUse = x => anm.Scripts.ContainsKey(x.Trim());
                    c.Filter = AnimNameFilter;
                    var oldName = sc.Key;
                    var script = sc.Value;
                    popups.OpenPopup(new NameInputPopup(c, sc.Key, newName =>
                    {
                        newName = newName.Trim();
                        if(oldName != newName)
                            undoBuffer.Commit(new RenameScript(script, anm, oldName, newName));
                    }));
                    break;
                }
            }
            ImGui.PopID();
        }
    }


    void WriteAnimations()
    {
        var destNode = hprefs.RootNode.Children.FirstOrDefault(x =>
            x.Name.Equals("animation", StringComparison.OrdinalIgnoreCase));
        var anm = ((CmpFile)drawable).Animation;
        if (anm == null || anm.Scripts.Count == 0)
        {
            if (destNode != null)
            {
                hprefs.RootNode.Children.Remove(destNode);
            }
            return;
        }
        if (destNode == null)
        {
            destNode = new LUtfNode() { Name = "Animation", Parent = hprefs.RootNode };
            hprefs.RootNode.Children.Add(destNode);
        }
        AnimationWriter.WriteAnimations(destNode, anm);
    }
}
