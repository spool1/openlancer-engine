// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package
using System;
using System.Collections.Generic;
using System.Numerics;
using LibreLancer.Data.Ini;
using LibreLancer.Data.Schema.Effects;

namespace LibreLancer.Data.Schema.Equipment;

[ParsedSection]
public partial class Explosion
{
    [Entry("nickname", Required = true)] public string Nickname = null!;
    // The last effect line. Effects has each effect line.
    public string? Effect;
    public List<(string Name, float? Strength)> Effects = [];
    // The minimum and the maximum lifetime.
    public Vector2 Lifetime;
    [Entry("process")] public string? Process;
    [Entry("strength")] public float Strength;
    [Entry("radius")] public float Radius;
    [Entry("hull_damage")] public float HullDamage;
    [Entry("energy_damage")] public float EnergyDamage;
    [Entry("impulse")] public float Impulse;

    [EntryHandler("lifetime", MinComponents = 1)]
    private void HandleLifetime(Entry e) => Lifetime = ExplosionLines.Lifetime(e);

    [EntryHandler("effect", MinComponents = 1, Multiline = true)]
    private void HandleEffect(Entry e)
    {
        var effect = ExplosionLines.Effect(e);
        Effects.Add(effect);
        Effect = effect.Name;
    }
}
