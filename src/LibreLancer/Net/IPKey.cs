namespace LibreLancer.Net;

using System;
using System.Net;
using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
public struct IPKey : IEquatable<IPKey>
{
    public ulong High;
    public ulong Low;

    public static unsafe IPKey From(IPAddress ip)
    {
        // Fetch IPv4 address so it is the same
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        IPKey key = default;
        IPKey* dst = &key;

        Span<byte> bytes = new(dst, 16);
        ip.TryWriteBytes(bytes, out _);

        return key;
    }

    public readonly bool Equals(IPKey other) =>
        High == other.High && Low == other.Low;

    public override readonly bool Equals(object? obj) =>
        obj is IPKey other && Equals(other);

    public override readonly int GetHashCode() =>
        HashCode.Combine(High, Low);
}
