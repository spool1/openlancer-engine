using System.Collections.Generic;
using System.Threading;

namespace LibreLancer.Net;

public class IPConnectionLimiter
{
    public int LimitPerAddress;
    private readonly Dictionary<IPKey, int> connections = new();
    private readonly Lock connectionsLock = new();

    public IPConnectionLimiter(int limitPerAddress)
    {
        LimitPerAddress = limitPerAddress;
    }

    public bool TryConnect(IPKey key)
    {
        if (LimitPerAddress <= 0)
            return true;
        lock (connectionsLock)
        {
            int count = connections.GetValueOrDefault(key, 0);
            if (count >= LimitPerAddress)
                return false;
            connections[key] = count + 1;
            return true;
        }
    }

    public void Disconnect(IPKey key)
    {
        lock (connectionsLock)
        {
            if (connections.TryGetValue(key, out var c))
            {
                c--;
                if (c == 0)
                    connections.Remove(key);
                else
                    connections[key] = c;
            }
        }
    }
}

