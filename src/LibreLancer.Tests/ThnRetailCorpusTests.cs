using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreLancer.Render;
using LibreLancer.Thn;
using LibreLancer.Thn.Events;
using LibreLancer.Thorn;
using Xunit;

namespace LibreLancer.Tests;

// Runs every cutscene in the retail data in the Thorn VM with the THN environment of the engine.
// The tests read the script tables, not ThnScript: a ThnScript with a motion path needs the native
// thorncompiler.
public class ThnRetailCorpusTests
{
    private static IEnumerable<(string Path, Dictionary<string, object> Output)> Scripts(RetailData data)
    {
        foreach (var path in Directory.EnumerateFiles(data.Folder, "*.thn", SearchOption.AllDirectories))
        {
            yield return (path,
                new ThornRunner(ThnScript.ThnEnv, data.ReadThornFile).DoBytes(File.ReadAllBytes(path), path));
        }
    }

    // A DEFORMABLE entity gets the skeleton of the costume that its template names, as in ThnScriptInstance.
    // An empty PART name is not checked, because GetAttachParent uses the root for it.
    [Fact]
    public void EveryBoneNameOnACharacterIsInItsCostume()
    {
        var data = RetailData.Open();
        var skeletons = new Dictionary<string, DfmSkeletonManager?>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();
        var checkedNames = 0;

        foreach (var (path, output) in Scripts(data))
        {
            var file = Path.GetFileName(path);
            var templates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entity in ((ThornTable)output["entities"]).Values.Cast<ThornTable>())
            {
                if (ThnTypes.Convert<EntityTypes>(entity["type"]) == EntityTypes.Deformable)
                {
                    templates[(string)entity["entity_name"]] = (string)entity["template_name"];
                }
            }

            void Check(float time, string entity, string? name, string kind)
            {
                if (!templates.TryGetValue(entity, out var template))
                {
                    return;
                }

                if (!skeletons.TryGetValue(template, out var skeleton))
                {
                    skeleton = data.LoadCostume(template);
                    skeletons[template] = skeleton;
                }

                checkedNames++;
                if (skeleton == null)
                {
                    failures.Add(FormattableString.Invariant($"{file} {time}: no costume {template} for {entity}"));
                }
                else if (skeleton.GetBone(name ?? "") == null)
                {
                    failures.Add(FormattableString.Invariant($"{file} {time}: {kind} '{name}' on {entity}"));
                }
            }

            foreach (var ev in ((ThornTable)output["events"]).Values.Cast<ThornTable>())
            {
                if (!ev.TryGetValue(4, out var p))
                {
                    continue;
                }

                var time = (float)ev[1];
                var targets = (ThornTable)ev[3];
                var props = (ThornTable)p;
                if (ThnTypes.Convert<EventTypes>(ev[2]) == EventTypes.StartIK)
                {
                    props.TryGetValue("end_effector", out var effector);
                    Check(time, (string)targets[1], (string?)effector, "end_effector");
                }

                if (targets.Length > 1 && props.TryGetValue("target_type", out var type) &&
                    ThnTypes.Convert<TargetTypes>(type) == TargetTypes.Part &&
                    props.TryGetValue("target_part", out var part) && !string.IsNullOrEmpty((string)part))
                {
                    Check(time, (string)targets[2], (string)part, "target_part");
                }
            }
        }

        // The 1506 retail scripts name 7799 bones on characters.
        Assert.Equal(7799, checkedNames);
        // Retail has one START_IK event with an empty end_effector. No bone has that name.
        Assert.Equal(["s072ab_offer_Quintaine_pl_09_Lab_01.thn 109.892: end_effector '' on Char_jacobi"], failures);
    }

    // StartIKEvent reads every retail START_IK event. 4808 of the 4825 events use only point_at.
    [Fact]
    public void EveryStartIKEventIsRead()
    {
        var count = 0;
        var pointAt = 0;
        foreach (var (path, output) in Scripts(RetailData.Open()))
        {
            foreach (var table in ((ThornTable)output["events"]).Values.Cast<ThornTable>())
            {
                if (ThnTypes.Convert<EventTypes>(table[2]) != EventTypes.StartIK)
                {
                    continue;
                }

                var ev = new StartIKEvent(table, path);
                count++;
                if (ev.PointAt && !ev.MoveTo)
                {
                    pointAt++;
                }

                Assert.Equal(2, ev.Targets.Length);
                Assert.True(Enum.IsDefined(ev.Up) && Enum.IsDefined(ev.Front), $"{path} {ev.Time}: {ev.Up} {ev.Front}");
            }
        }

        Assert.Equal(4825, count);
        Assert.Equal(4808, pointAt);
    }
}
