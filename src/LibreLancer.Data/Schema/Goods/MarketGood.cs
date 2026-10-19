// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;
using LibreLancer.Data.Ini;

namespace LibreLancer.Data.Schema.Goods;

public class MarketGood
{
    public string? Good;
    public int Rank;
    public float Rep;
    public int Min; //Unused
    public int Max; //Unused
    public bool Preserve; //Only for equipment
    // The sixth value: 0 the base buys and sells the good, 1 the base only buys it, 2 the base only sells it.
    public int TransactionType;
    public float Multiplier;

    public MarketGood() { }
    public MarketGood(Entry e)
    {
        Good = e[0].ToString();
        Rank = e[1].ToInt32();
        Rep = e[2].ToSingle();
        Min = e[3].ToInt32();
        Max = e[4].ToInt32();
        Preserve = e[5].ToInt32() > 0;
        TransactionType = e[5].ToInt32();
        Multiplier = e[6].ToSingle();
    }
}
