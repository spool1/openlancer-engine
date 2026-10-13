using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using LancerEdit.GameContent.Lookups;
using LibreLancer.ContentEdit.Model;
using LibreLancer.ImUI;
using LibreLancer.Utf;
using LibreLancer.Utf.Anm;

namespace LancerEdit;

public class AnimationImportPopup : PopupWindow
{
    public override string Title { get; set;  } = "Add Animations from File";

    public override Vector2 InitSize => new Vector2(500,300) * ImGuiHelper.Scale;

    class SelectedScript(Script sc)
    {
        public bool Selected = true;
        public Script Script = sc;
    }

    class ConstructMap(string con)
    {
        public string Construct = con;
        public AbstractConstruct? Target;
    }

    private List<SelectedScript> scripts = new();
    private List<ConstructMap> allMapped = new();
    private List<ConstructMap> selectedMapped = new();
    private ObjectLookup<AbstractConstruct> targets;
    private bool canImport = true;
    private int selectionCount;
    private Action<Script[]> onAdd;


    public AnimationImportPopup(AnmFile source, AbstractConstruct[] tgtConstructs, Action<Script[]> onAdd)
    {
        this.onAdd = onAdd;

        HashSet<string> allCons = new(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in source.Scripts.Values)
        {
            foreach (var jm in sc.JointMaps)
            {
                allCons.Add(jm.ChildName);
            }
            scripts.Add(new(sc));
        }
        foreach (var c in allCons)
        {
            var mapping = new ConstructMap(c);
            mapping.Target =
                tgtConstructs.FirstOrDefault(x => x.ChildName.Equals(c, StringComparison.OrdinalIgnoreCase));
            allMapped.Add(mapping);
        }
        allMapped.Sort((x, y) => string.Compare(x.Construct, y.Construct, StringComparison.Ordinal));
        selectedMapped.AddRange(allMapped);
        targets = new(tgtConstructs, x => x.ChildName);
        selectionCount = scripts.Count;
        ComputeAllowed();
    }

    void ComputeAllowed()
    {
        canImport = true;
        foreach (var m in selectedMapped)
        {
            if (m.Target == null)
            {
                canImport = false;
                return;
            }
        }
    }

    void ReloadConstructMap()
    {
        selectionCount = 0;
        selectedMapped = new();
        HashSet<string> allCons = new(StringComparer.OrdinalIgnoreCase);
        foreach (var sc in scripts)
        {
            if (!sc.Selected)
                continue;
            selectionCount++;
            foreach (var jm in sc.Script.JointMaps)
            {
                allCons.Add(jm.ChildName);
            }
        }
        foreach (var m in allMapped)
        {
            if(allCons.Contains(m.Construct))
                selectedMapped.Add(m);
        }
        ComputeAllowed();
    }

    void ScriptsColumn()
    {
        foreach (var sc in scripts)
        {
            if (ImGui.Checkbox(sc.Script.Name, ref sc.Selected))
            {
                ReloadConstructMap();
            }
        }
    }

    void ConstructsColumn()
    {
        if (ImGui.BeginTable("##cons", 2, ImGuiTableFlags.BordersInner | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Source");
            ImGui.TableSetupColumn("Target", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var sc in selectedMapped)
            {
                ImGui.PushID(sc.Construct);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(sc.Construct);
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                targets.Draw("##tgt", ref sc.Target, (_, _) => ComputeAllowed());
                ImGui.PopID();
            }
            ImGui.EndTable();
        }
    }

    Script[] CreateScripts()
    {
        List<Script> newScripts = new();
        var mapping = new Dictionary<string, AbstractConstruct>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in selectedMapped)
        {
            mapping[m.Construct] = m.Target!;
        }
        foreach (var sc in scripts)
        {
            if (!sc.Selected)
                continue;
            var ns = new Script(sc.Script.Name);
            foreach (var jm in sc.Script.JointMaps)
            {
                var njm = new JointMap();
                var mapped = mapping[jm.ChildName];
                njm.ChildName = mapped.ChildName;
                njm.ParentName = mapped.ParentName;
                njm.Channel = jm.Channel;
                var buf = new EditableAnmBuffer(ref njm.Channel);
                njm.Channel.SetBuffer(buf);
                ns.JointMaps.Add(njm);
            }
            newScripts.Add(ns);
        }
        return newScripts.ToArray();
    }

    public override void Draw(bool appearing)
    {
        if (ImGuiExt.Button("Add Selected", canImport && selectionCount > 0))
        {
            onAdd(CreateScripts());
            ImGui.CloseCurrentPopup();
        }
        if(!canImport)
            ImGui.SetItemTooltip("Not all constructs mapped.");
        else if (selectionCount <= 0)
            ImGui.SetItemTooltip("No scripts selected.");
        if (ImGui.BeginTable("##items", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Scripts", 0, 0.35f);
            ImGui.TableSetupColumn("Constructs", ImGuiTableColumnFlags.WidthStretch, 0.65f);
            ImGui.TableHeadersRow();
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.BeginChild("Scripts"))
            {
                ScriptsColumn();
            }
            ImGui.EndChild();
            ImGui.TableNextColumn();
            if (selectionCount > 0)
            {
                if (ImGui.BeginChild("Constructs"))
                {
                    ConstructsColumn();
                }
                ImGui.EndChild();
            }
            ImGui.EndTable();
        }
    }
}
