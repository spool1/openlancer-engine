using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Effects;

[ParsedSection]
public partial class Explosion
{
    [Entry("nickname", Required = true)] public string Nickname = null!;
    // The minimum and the maximum lifetime.
    public Vector2 Lifetime;
    [Entry("process")] public string? Process;
    [Entry("radius")] public float Radius;
    [Entry("hull_damage")] public float HullDamage;
    [Entry("energy_damage")] public float EnergyDamage;
    [Entry("num_child_pieces")] public int NumChildPieces;
    [Entry("debris_impulse")] public float DebrisImpulse;
    [Entry("innards_debris_start_time")] public float InnardsDebrisStartTime;
    [Entry("innards_debris_num")] public int InnardsDebrisNum;
    [Entry("innards_debris_radius")] public float InnardsDebrisRadius;
    [Entry("innards_debris_object", Multiline = true)]
    public List<string> InnardsDebrisObjects = [];

    // The last effect line. Effects has each effect line.
    public string? Effect;
    public float EffectSParam; // guess
    public List<(string Name, float? Strength)> Effects = [];

    public List<(string Name, float Weight)> DebrisTypes = [];

    [EntryHandler("lifetime", MinComponents = 1)]
    private void HandleLifetime(Entry e) => Lifetime = ExplosionLines.Lifetime(e);

    [EntryHandler("effect", MinComponents = 1, Multiline = true)]
    private void HandleEffect(Entry e)
    {
        var effect = ExplosionLines.Effect(e);
        Effects.Add(effect);
        Effect = effect.Name;
        EffectSParam = effect.Strength ?? 0.0f;
    }

    [EntryHandler("debris_type", MinComponents = 2, Multiline = true)]
    private void HandleDebrisType(Entry e)
    {
        DebrisTypes.Add((e[0].ToString(), e[1].ToSingle()));
    }
}
