// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using LibreLancer;
using LibreLancer.ContentEdit;
using LibreLancer.ContentEdit.Model;
using LibreLancer.Utf;
using LibreLancer.Utf.Cmp;

namespace LancerEdit.ModelEditor
{
    public partial class ModelEditorTab
    {
        List<ConstructEditor> partEditors = new List<ConstructEditor>();
        void AddPartEditor(AbstractConstruct p)
        {
            partEditors.Add(new ConstructEditor(p, this, undoBuffer));
        }

        void PartEditor()
        {
            List<ConstructEditor> toRemove = new List<ConstructEditor>();
            foreach(var ed in partEditors) {
                ed.PartEditor();
                if (!ed.Open)
                    toRemove.Add(ed);
            }
            foreach (var ed in toRemove)
                partEditors.Remove(ed);
        }

        void WriteConstructs()
        {
            if (!(drawable is CmpFile))
                return;
            FixConstructor fix = null;
            RevConstructor rev = null;
            PrisConstructor pris = null;
            SphereConstructor sphere = null;

            foreach (var p in vmsModel.AllParts)
            {
                var con = p.Construct;
                if (con is FixConstruct)
                {
                    if (fix == null) fix = new FixConstructor();
                    fix.Add((FixConstruct)con);
                }
                else if (con is RevConstruct)
                {
                    if (rev == null) rev = new RevConstructor();
                    rev.Add((RevConstruct)con);
                }
                else if (con is PrisConstruct)
                {
                    if (pris == null) pris = new PrisConstructor();
                    pris.Add((PrisConstruct)con);
                }
                else if (con is SphereConstruct)
                {
                    if (sphere == null) sphere = new SphereConstructor();
                    sphere.Add((SphereConstruct)con);
                }
            }

            hprefs.Cons.Children = new List<LUtfNode>();
            if(fix != null) {
                hprefs.Cons.Children.Add(new LUtfNode() {
                    Name = "Fix", Parent = hprefs.Cons, Data = fix.GetData()
                });
            }
            if(rev != null) {
                hprefs.Cons.Children.Add(new LUtfNode() {
                    Name = "Rev", Parent = hprefs.Cons, Data = rev.GetData()
                });
            }
            if(pris != null) {
                hprefs.Cons.Children.Add(new LUtfNode() {
                    Name = "Pris", Parent = hprefs.Cons, Data = pris.GetData()
                });
            }
            if(sphere != null) {
                hprefs.Cons.Children.Add(new LUtfNode() {
                    Name = "Sphere", Parent = hprefs.Cons, Data = sphere.GetData()
                });
            }
        }

    }
    class ConstructEditor
    {
        public bool Open {
            get {
                return editingPart != null;
            }
        }
        ModelEditorTab mv;
        private EditorUndoBuffer undoBuffer;
        public ConstructEditor(AbstractConstruct part, ModelEditorTab mv, EditorUndoBuffer undoBuffer)
        {
            editingPart = part;
            this.mv = mv;
            this.undoBuffer = undoBuffer;
        }
        AbstractConstruct editingPart = null;
        bool partEditorOpen = false;
        bool partFirst = false;

        public unsafe void PartEditor()
        {
            if (editingPart == null) return;
            if(editingPart != null && !partEditorOpen) {
                partEditorOpen = true;
                partFirst = true;
            }
            if(ImGui.Begin("Part Editor##" + editingPart.ChildName,ref partEditorOpen,partFirst ? ImGuiWindowFlags.AlwaysAutoResize : ImGuiWindowFlags.None)) {
                partFirst = false;
                ImGui.Text(editingPart.ChildName);
                ImGui.Text("Type: " + ModelEditorTab.ConType(editingPart));

                if (Controls.BeginEditorTable("parteditor"))
                {
                    Controls.InputFloat3Undo("Position", undoBuffer, () => ref editingPart.Origin);
                    Controls.InputEulerQuaternionUndo("Rotation", undoBuffer, () => ref editingPart.Rotation);
                    if (editingPart is RevConstruct rev)
                    {
                        Controls.InputFloat3Undo("Offset", undoBuffer, () => ref rev.Offset, "%.5f");
                        Controls.InputFloat3Undo("Axis", undoBuffer, () => ref rev.AxisRotation, "%.5f");
                        Controls.AxisSelectionUndo("Axis", undoBuffer, () => ref rev.AxisRotation);
                        Controls.InputAngleUndo("Min", undoBuffer, () => ref rev.Min);
                        Controls.InputAngleUndo("Max", undoBuffer, () => ref rev.Max);
                    }
                    else if (editingPart is PrisConstruct pris)
                    {
                        Controls.InputFloat3Undo("Offset", undoBuffer, () => ref pris.Offset, "%.5f");
                        Controls.InputFloat3Undo("Axis", undoBuffer, () => ref pris.AxisTranslation, "%.5f");
                        Controls.AxisSelectionUndo("Axis", undoBuffer, () => ref pris.AxisTranslation);
                        Controls.InputFloatUndo("Min", undoBuffer, () => ref pris.Min, null, "%.5f");
                        Controls.InputFloatUndo("Max", undoBuffer, () => ref pris.Max, null, "%.5f");
                    }
                    else if (editingPart is SphereConstruct sphere)
                    {
                        Controls.InputFloat3Undo("Offset", undoBuffer, () => ref sphere.Offset, "%.5f");
                        Controls.InputFloat3Undo("Min", undoBuffer, () => ref sphere.Min, "%.5f");
                        Controls.InputFloat3Undo("Max", undoBuffer, () => ref sphere.Min, "%.5f");
                    }
                    Controls.EndEditorTable();
                }
            }
            ImGui.End();
            if (!partEditorOpen) {
                editingPart.OverrideTransform = null;
                editingPart = null;
            }
        }
    }
}
