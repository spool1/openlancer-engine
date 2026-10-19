using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Effects;

// The lifetime and effect lines of an [Explosion]. Retail reads the explosions of the effect files and of the
// equipment, solar and ship files with one reader, so both schema classes use these methods.
internal static class ExplosionLines
{
    // lifetime = min[, max]. One value sets the minimum and the maximum.
    public static Vector2 Lifetime(Entry e)
    {
        ParseHelpers.ComponentCheck(2, e.Section, e, 1);
        var min = e[0].ToSingle();
        return new Vector2(min, e.Count > 1 ? e[1].ToSingle() : min);
    }

    // effect = <effect>[, strength]. Each line adds an effect. The strength is null when the line has no strength.
    public static (string Name, float? Strength) Effect(Entry e)
    {
        ParseHelpers.ComponentCheck(2, e.Section, e, 1);
        return (e[0].ToString(), e.Count > 1 ? e[1].ToSingle() : null);
    }
}
