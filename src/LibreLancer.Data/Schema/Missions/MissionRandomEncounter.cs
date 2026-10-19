using System.Collections.Generic;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Missions;

// [MsnRandEnc]: a random attack encounter that a mission owns. The mission enables and disables it by its nickname.
// encounter_type is TL_Attack (on a trade lane) or SP_Attack (in space). The encounter reads the other keys of the
// section itself, so this class keeps them as raw entries. The mission runtime does not use the section yet. Retail
// keeps a section without a nickname: its name is empty.
[ParsedSection]
public partial class MissionRandomEncounter : IEntryHandler
{
    [Entry("nickname")]
    public string Nickname = "";
    [Entry("encounter_type")]
    public string? EncounterType;

    // The keys of the encounter, in the order of the file.
    public List<Entry> Entries = [];

    bool IEntryHandler.HandleEntry(Entry e)
    {
        Entries.Add(e);
        return true;
    }
}
