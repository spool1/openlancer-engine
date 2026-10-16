// MIT License - Copyright (c) Callum McGing
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

namespace LibreLancer.Net
{
	public static class LNetConst
	{
		public const int DefaultPort = 43443;
        public const int BroadcastPort = 42112;
        public const uint PingMagic = 0xBACAFEBA;
		public const string DefaultAppIdent = "LIBRELANCER";
        public const ulong BroadcastKey = 0xBEEFBEEFCAFECAFE;
        public const uint DisconnectMagic = 0xFEFEBBFE;
        public const int CharsPerAccount = 10;
        public const int AccountRateLimitMs = 3000;
        public const int AccountWindowLimit = 30;
        public const int AccountCreationWindowHours = 1;
        public const long AccountCreationWindowMs = AccountCreationWindowHours * 3600000;
    }
}
