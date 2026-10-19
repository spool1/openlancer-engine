using System;
using System.Collections.Generic;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Universe.Rooms;
public record SceneEntry(bool AmbientAll, bool TrafficPriority, string Path);

[ParsedSection]
public partial class RoomInfo
{
    [Entry("set_script")]
    public string? SetScript;

    public List<SceneEntry> SceneScripts = [];

    [Entry("goodscart_script")]
    public string? GoodscartScript;

    // Each animation line adds a looping animation, and each animation_oneshot line one that plays one time. Retail
    // keeps up to 32. The engine does not play room animations yet.
    [Entry("animation", Multiline = true)]
    public List<string> Animations = [];
    [Entry("animation_oneshot", Multiline = true)]
    public List<string> OneShotAnimations = [];

    [EntryHandler("scene", MinComponents = 2, Multiline = true)]
    private void Scene(Entry e)
    {
        try
        {
            int i = 0;
            bool all = false;
            if (e[0].ToString().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                all = true;
                i++;
            }

            if (!e[i].ToString().Equals("ambient", StringComparison.OrdinalIgnoreCase))
            {
                FLLog.Warning("Ini", $"Invalid room scene entry {e}");
            }

            i++;
            var path = e[i].ToString();
            var trafficPriority = (i + 1 < e.Count) &&
                              e[i + 1].ToString().Equals("TRAFFIC_PRIORITY", StringComparison.OrdinalIgnoreCase);
            SceneScripts.Add(new SceneEntry(all, trafficPriority, path));
        }
        catch
        {
            FLLog.Error("Ini", $"Bad formatting for scene entry {e}");
        }
    }
}
