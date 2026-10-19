// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package
using System;
using LibreLancer.Data.Ini;
namespace LibreLancer.Data.Schema.Solar;

public enum DockSphereType
{
    berth,
    moor_small,
    moor_medium,
    moor_large,
    ring,
    airlock,
    jump
}

public record DockSphere(DockSphereType Type, string Hardpoint, int Radius, string? Script)
{
    // docking_sphere = <type>, <hardpoint>, <radius>[, <animation>]. Solars and ships read the line in the same way.
    public static DockSphere FromEntry(Entry e)
    {
        string? scr = e.Count == 4 ? e[3].ToString() : null;
        if (!Enum.TryParse<DockSphereType>(e[0].ToString(), true, out var type))
        {
            IniDiagnostic.InvalidEnum(e, e.Section);
        }

        return new DockSphere(type, e[1].ToString(), e[2].ToInt32(), scr);
    }
}
