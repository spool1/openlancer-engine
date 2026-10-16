using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace LibreLancer.Net.Protocol;

public struct AuthenticationInfo
{
    public AuthenticationKind Kind;
    public byte Length;
    public Storage Data;

    [InlineArray(64)]
    public struct Storage
    {
        private byte _value;
    }

    public AuthenticationInfo(Guid guid)
    {
        Kind = AuthenticationKind.GUID;
        Length = 16;
        guid.TryWriteBytes(Data);
    }

    public AuthenticationInfo(string str)
    {
        Kind = AuthenticationKind.Token;
        if (!Encoding.UTF8.TryGetBytes(
                str, Data, out var len))
        {
            throw new InvalidOperationException("String too large");
        }
        Length = (byte)len;
    }

    public bool TryGetGuid(out Guid guid)
    {
        guid = Guid.Empty;
        if (Kind != AuthenticationKind.GUID ||
            Length != 16)
            return false;
        guid = new Guid(((ReadOnlySpan<byte>)Data).Slice(0, 16));
        return true;
    }

    public string GetString()
    {
        return Encoding.UTF8.GetString(((ReadOnlySpan<byte>)Data).Slice(0, Length));
    }

    public void Put(PacketWriter writer)
    {
        writer.Put((byte)Kind);
        writer.Put(Length);
        for(int i = 0; i < Length; i++)
            writer.Put(Data[i]);
    }

    public static bool TryGet(
        PacketReader reader,
        out AuthenticationInfo info)
    {
        info = default;
        if (!reader.TryGetByte(out var authKind))
        {
            return false;
        }

        info.Kind = (AuthenticationKind)authKind;
        if (info.Kind != AuthenticationKind.GUID &&
            info.Kind != AuthenticationKind.Token)
        {
            return false;
        }

        if (!reader.TryGetByte(out info.Length) ||
            info.Length > 64)
            return false;
        for (int i = 0; i < info.Length; i++)
        {
            if (!reader.TryGetByte(out info.Data[i]))
                return false;
        }
        return true;
    }


}
