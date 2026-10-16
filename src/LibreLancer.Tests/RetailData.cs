using System;
using System.IO;
using LibreLancer.Data.IO;
using LibreLancer.Data.Schema;
using LibreLancer.Data.Schema.Characters;
using LibreLancer.Render;
using LibreLancer.Resources;
using LibreLancer.Utf.Anm;
using LibreLancer.Utf.Dfm;
using LibreLancer.World;
using Xunit;

namespace LibreLancer.Tests;

// The retail Freelancer data. OPENLANCER_BASE gives the folder that contains DATA and EXE.
// A test that uses this data is skipped when the folder does not exist.
public class RetailData
{
    private const string DefaultFolder = @"D:\Projects\Freelancer Decompiled\GameData\base";

    public readonly string Folder;
    public readonly FileSystem VFS;
    private readonly FreelancerIni ini;
    private readonly CostumesIni costumes;
    private readonly BodypartsIni bodyparts;
    private readonly ServerResourceManager resources;

    private RetailData(string folder)
    {
        Folder = folder;
        VFS = FileSystem.FromPath(folder);
        ini = new FreelancerIni(VFS);
        costumes = new CostumesIni(ini.CostumesPath!, VFS);
        bodyparts = new BodypartsIni(ini.BodypartsPath!, new FreelancerData(ini, VFS));
        resources = new ServerResourceManager(null, VFS);
    }

    public static RetailData Open()
    {
        var folder = Environment.GetEnvironmentVariable("OPENLANCER_BASE");
        if (string.IsNullOrEmpty(folder))
        {
            folder = DefaultFolder;
        }

        Assert.SkipUnless(Directory.Exists(folder), $"No retail data in {folder}. Set OPENLANCER_BASE.");
        return new RetailData(folder);
    }

    // Reads a file that a THN script includes, as GameItemDb.ThornReadCallback does.
    public byte[] ReadThornFile(string file) => VFS.ReadAllBytes("EXE/" + file);

    // Makes the skeleton of a costume, as ThnScriptInstance does for a DEFORMABLE entity.
    public DfmSkeletonManager? LoadCostume(string nickname)
    {
        var costume = costumes.FindCostume(nickname);
        var body = LoadBodypart(costume?.Body);
        if (costume == null || body == null)
        {
            return null;
        }

        return new DfmSkeletonManager(body, LoadBodypart(costume.Head), LoadBodypart(costume.LeftHand),
            LoadBodypart(costume.RightHand));
    }

    // Makes the skeleton of a costume and loops an animation on it. The pose is the pose at 0.5 s.
    public DfmSkeletonManager LoadPosedCostume(string nickname, string animations, string script)
    {
        var skeleton = LoadCostume(nickname)!;
        skeleton.StartScript(LoadAnimations(animations).Scripts[script], 0, 1, 0, true);
        skeleton.UpdateScripts(0.5);
        return skeleton;
    }

    private DfmFile? LoadBodypart(string? nickname)
    {
        var mesh = nickname == null ? null : bodyparts.FindBodypart(nickname)?.Mesh;
        return mesh == null ? null : (DfmFile?)resources.GetDrawable(ini.DataPath + mesh)?.Drawable;
    }

    // Makes the object of a rigid model, as ThnScriptInstance does for a COMPOUND entity. The object draws nothing.
    public GameObject LoadModel(string path) =>
        new(resources.GetDrawable(ini.DataPath + path)!, resources, false, false);

    // Loads one file of character animations, as GameDataManager.GetCharacterAnimations does.
    public AnmFile LoadAnimations(string path)
    {
        var anm = new AnmFile();
        using var stream = VFS.Open(ini.DataPath + path);
        AnmFile.ParseToTable(anm.Scripts, anm.Buffer, new StringDeduplication(), stream, path);
        anm.Buffer.Commit();
        return anm;
    }
}
