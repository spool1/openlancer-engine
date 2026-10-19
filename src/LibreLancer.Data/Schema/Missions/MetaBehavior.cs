using System;
using System.Collections.Generic;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Missions;

// [MetaBehavior]: a named list of guide lines that a mission gives to a ship. MB_GotoGuide sends the ship to a target
// or a position, and MB_DockGuide makes it dock. This class keeps each guide line as a raw entry. The game ignores the
// system key and each other key. The mission runtime does not use the section yet.
[ParsedSection]
public partial class MetaBehavior : IEntryHandler
{
    [Entry("nickname", Required = true)]
    public string Nickname = null!;
    [Entry("system")]
    public string? System;

    // The MB_GotoGuide and MB_DockGuide lines, in the order of the file.
    public List<Entry> Guides = [];

    bool IEntryHandler.HandleEntry(Entry e)
    {
        if (!e.Name.Equals("MB_GotoGuide", StringComparison.OrdinalIgnoreCase) &&
            !e.Name.Equals("MB_DockGuide", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Guides.Add(e);
        return true;
    }
}
