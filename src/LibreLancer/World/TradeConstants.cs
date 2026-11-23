// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

namespace LibreLancer.World
{
    public static class TradeConstants
    {
        public const double SHIP_RESALE_MULTIPLIER = 0.8;
        public const double EQUIP_RESALE_MULTIPLIER = 0.5;

        /// <summary>What a base pays for a ship of a price (OL-0080): the price times
        /// <see cref="SHIP_RESALE_MULTIPLIER"/>.</summary>
        public static ulong ShipResale(long price) => (ulong) (price * SHIP_RESALE_MULTIPLIER);
    }
}