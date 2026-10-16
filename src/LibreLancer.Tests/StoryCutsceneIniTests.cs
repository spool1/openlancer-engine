using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibreLancer.Data.Ini;
using LibreLancer.Data.Schema.Missions;
using Xunit;

namespace LibreLancer.Tests;

// Reads the [CharacterEncounter] sections of the retail RTC INIs, as CGameSession does for an Act_AddRTC.
public class StoryCutsceneIniTests
{
    // Retail gives the decline script of an offer with the key "decline" or the key "reject".
    // RoomGameplay plays StoryEncounter.Reject when the player declines the offer.
    [Theory]
    [InlineData(@"MISSIONS\M03\M003_s014a_Li02_02_offer.ini", @"scripts\story\s014c_decline_juni_pl_03_Bar_01.thn")]
    [InlineData(@"MISSIONS\M13\M013_s072d_St01_01_reoffer.ini",
        @"scripts\story\s072c_decline_Quintaine_pl_09_lab_01.thn")]
    [InlineData(@"MISSIONS\M02\M002_s009a_Li01_01_offer.ini", @"scripts\story\s009c_decline_King_li_01_Bar_01.thn")]
    public void AnOfferReadsItsDeclineScriptFromEitherKey(string rtc, string script)
    {
        var data = RetailData.Open();
        var encounter = Assert.Single(new StoryCutsceneIni(data.DataFilePath(rtc), data.VFS).Encounters);

        Assert.Equal(script, encounter.Reject);
    }

    // Compares each parsed encounter with the raw keys of its section. 19 retail sections use "decline" and 4 use
    // "reject". Each decline script exists, so RoomGameplay can load it.
    [Fact]
    public void EveryRetailDeclineScriptIsRead()
    {
        var data = RetailData.Open();
        var keys = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(data.Folder, "DATA", "MISSIONS"), "*.ini",
                     SearchOption.AllDirectories))
        {
            var path = Path.GetRelativePath(data.Folder, file);
            var sections = IniFile.ParseFile(path, data.VFS)
                .Where(x => x.Name.Equals("CharacterEncounter", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sections.Count == 0)
            {
                continue;
            }

            var encounters = new StoryCutsceneIni(path, data.VFS).Encounters;
            Assert.Equal(sections.Count, encounters.Count);
            foreach (var (section, encounter) in sections.Zip(encounters))
            {
                var entry = section.SingleOrDefault(x => IsDeclineKey(x.Name));
                Assert.Equal(entry?[0].ToString(), encounter.Reject);
                if (entry != null)
                {
                    Assert.True(data.VFS.FileExists(data.DataFilePath(encounter.Reject!)),
                        $"{path}: {encounter.Reject}");
                    keys.Add(entry.Name.ToLowerInvariant());
                }
            }
        }

        Assert.Equal(["decline: 19", "reject: 4"],
            keys.GroupBy(x => x).OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Count()}"));
    }

    private static bool IsDeclineKey(string name) =>
        name.Equals("decline", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("reject", StringComparison.OrdinalIgnoreCase);
}
