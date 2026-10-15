// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using LibreLancer;
using LibreLancer.ImUI;
using LibreLancer.Utf.Cmp;
using LibreLancer.World;

namespace LancerEdit.ModelEditor
{
    public partial class ModelEditorTab
    {
        Hardpoint hpEditing;
        void DeleteHardpoint(Hardpoint hpDelete, RigidModelPart hpDeleteFrom)
        {
            popups.MessageBox("Confirm?", $"Are you sure you wish to delete '{hpDelete.Name}'?",
                false, MessageBoxButtons.YesNo, r =>
                {
                    if (r == MessageBoxResponse.Yes)
                    {
                        var gz = gizmos.Where((x) => x.Hardpoint == hpDelete).First();
                        undoBuffer.Commit(new RemoveHardpoint(hpDelete, gz, hpDeleteFrom, this));
                    }
                });
        }

        IEnumerable<string> HardpointNames() => gizmos.Select(gz => gz.Hardpoint.Definition.Name);

        string GetDupName(string name)
        {
            foreach(var hp in HardpointInformation.All())
            {
                if(name.StartsWith(hp.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (hp.Autoname == HpNaming.None)
                        return GetCopyName(name);
                    else if (hp.Autoname == HpNaming.Letter)
                        return (hp.Name + HardpointInformation.GetHpLettering(hp.Name, HardpointNames()));
                    else if (hp.Autoname == HpNaming.Number)
                        return (hp.Name + HardpointInformation.GetHpNumbering(hp.Name, HardpointNames())
                            .ToString("00"));
                }
            }
            return GetCopyName(name);
        }
        string GetCopyName(string name)
        {
            var src = name + "_Copy";
            foreach(var gz in gizmos)
            {
                if (gz.Hardpoint.Definition.Name.Equals(src, StringComparison.OrdinalIgnoreCase))
                    return GetCopyName(src);
            }
            return src;
        }

        void NewHardpoint(bool newIsFixed, RigidModelPart addTo)
        {
            popups.OpenPopup(new NewHardpointPopup(newIsFixed,
                HardpointNames,
                name =>
                {
                    HardpointDefinition def;
                    if (newIsFixed) def = new FixedHardpointDefinition(name);
                    else def = new RevoluteHardpointDefinition(name);
                    var hp = new Hardpoint(def, addTo);
                    undoBuffer.Commit(new AddHardpoint(hp, new HardpointGizmo(hp, addTo), addTo, this));
                }));
        }


        class RemoveHardpoint(
            Hardpoint hp,
            HardpointGizmo gizmo,
            RigidModelPart removeFrom,
            ModelEditorTab tab)
            : EditorAction
        {
            public override void Commit()
            {
                removeFrom.Hardpoints.Remove(hp);
                tab.gizmos.Remove(gizmo);
                if (tab.hpEditing == hp)
                {
                    tab.hpEditing = null;
                }
            }

            public override void Undo()
            {
                removeFrom.Hardpoints.Add(hp);
                tab.gizmos.Add(gizmo);
            }

            public override string ToString() => $"Remove Hardpoint '{hp.Name}'";
        }

        class AddHardpoint(
            Hardpoint hp,
            HardpointGizmo gizmo,
            RigidModelPart addTo,
            ModelEditorTab tab)
            : EditorAction
        {
            public override void Commit()
            {
                addTo.Hardpoints.Add(hp);
                tab.gizmos.Add(gizmo);
            }

            public override void Undo()
            {
                addTo.Hardpoints.Remove(hp);
                tab.gizmos.Remove(gizmo);
                if (tab.hpEditing == hp)
                {
                    tab.hpEditing = null;
                }
            }

            public override string ToString() => $"Add Hardpoint '{hp.Name}'";
        }

        bool hpEditOpen = false;
        HardpointGizmo editingGizmo;
        unsafe void HardpointEditor()
        {
            if (hpEditing == null)
            {
                hpEditOpen = false;
                return;
            }
            if (hpEditing != null && hpEditOpen == false)
            {
                editingGizmo = gizmos.First((x) => x.Hardpoint == hpEditing);
                editingGizmo.Editing = true;
                hpEditOpen = true;
            }
            ImGui.SetNextWindowSize(new Vector2(350) * ImGuiHelper.Scale, ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Hardpoint Editor##" + Unique, ref hpEditOpen))
            {
                ImGui.Text(hpEditing.Name);
                if (Controls.BeginEditorTable("##hardpoint"))
                {
                    Controls.InputFloat3Undo("Position", undoBuffer, () => ref hpEditing.Definition.Position, "%.5f");
                    Controls.InputEulerRotationMatrixUndo("Rotation", undoBuffer, () =>
                        ref hpEditing.Definition.Orientation);
                    if (hpEditing.Definition is RevoluteHardpointDefinition rev)
                    {
                        Controls.InputFloat3Undo("Axis", undoBuffer, () => ref rev.Axis, "%.5f");
                        Controls.AxisSelectionUndo("Axis", undoBuffer, () => ref rev.Axis);
                        Controls.InputAngleUndo("Min", undoBuffer, () => ref rev.Min, null, "%.5f");
                        Controls.InputAngleUndo("Max", undoBuffer, () => ref rev.Max, null, "%.5f");
                    }
                    Controls.EndEditorTable();
                }
                hpEditing.RefreshValues();
            }
            ImGui.End();
            if (hpEditOpen == false)
            {
                hpEditing = null;
                editingGizmo.Editing = false;
            }
        }

        private bool manipulatingHp;
        private Matrix4x4 hpStartRotation;
        private Vector3 hpEndRotation;

        bool ManipulateHardpoint(ICamera camera)
        {
            if (hpEditing == null || hpEditOpen == false)
                return false;
            var v = camera.View;
            var p = camera.Projection;

            var mode = ImGui.GetIO().KeyCtrl ? GuizmoMode.WORLD : GuizmoMode.LOCAL;
            Matrix4x4 delta = Matrix4x4.Identity;
            var hpTransform = hpEditing.TransformNoRotate.Matrix();
            var editingTransform = hpTransform * GetModelMatrix();
            GuizmoOp op;

            if ((op = ImGuizmo.Manipulate(ref v, ref p, GuizmoOperation.TRANSLATE | GuizmoOperation.ROTATE_AXIS, mode,
                    ref editingTransform, out delta)) != GuizmoOp.Nothing && !delta.IsIdentity)
            {
                if (!manipulatingHp)
                {
                    hpStartRotation = hpEditing.Definition.Orientation;
                    hpEndRotation = hpEditing.Definition.Position;
                    manipulatingHp = true;
                }

                var tr = Transform3D.FromMatrix(ImGuizmo.ApplyDelta(hpEditing.HpTransformInfo.Matrix(), delta, op));
                hpEditing.Definition.Position = tr.Position;
                hpEditing.Definition.Orientation = Matrix4x4.CreateFromQuaternion(tr.Orientation);
                hpEditing.RefreshValues();
            }

            //Insert undo
            if (!ImGuizmo.IsUsing() && manipulatingHp)
            {
                var pos = EditorPropertyModification<Vector3>.Create(
                    "Position", () => ref hpEditing.Definition.Position, hpEndRotation,
                    hpEditing.Definition.Position);
                var orient = EditorPropertyModification<Matrix4x4>.Create(
                    "Rotation", () => ref hpEditing.Definition.Orientation, hpStartRotation,
                    hpEditing.Definition.Orientation);
                undoBuffer.Commit(EditorAggregateAction.Create([pos,orient]));
                manipulatingHp = false;
            }

            return ImGuizmo.IsOver() || ImGuizmo.IsUsing();
        }
    }
}
