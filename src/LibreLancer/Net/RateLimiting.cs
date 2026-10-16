using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LibreLancer.Server;

namespace LibreLancer.Net;

public class RateLimiting
{
    record struct WindowLimit(long WindowStart, int Count);

    private readonly Stopwatch time;
    private readonly Lock rateLimitLock = new();
    private readonly Lock windowLock = new();
    private readonly Dictionary<IPKey, long> rateLimits = new();
    private readonly Dictionary<IPKey, WindowLimit> createdAccounts = new();

    private long lastRateLimitClean = 0;
    private long lastWindowRateLimitClean = 0;

    public readonly bool Enabled;

    public RateLimiting(Stopwatch time, bool enabled)
    {
        this.time = time;
        Enabled = enabled;
    }

    public bool TryCreate(Player player)
    {
        if (!Enabled)
            return true;
        var key = player.Key;
        lock (windowLock)
        {
            long now = time.ElapsedMilliseconds;
            if (!createdAccounts.TryGetValue(key, out var entry) ||
                now - entry.WindowStart >= LNetConst.AccountCreationWindowMs)
            {
                createdAccounts[key] = new WindowLimit(now, 1);
                return true;
            }
            if (entry.Count >= LNetConst.AccountWindowLimit)
                return false;
            entry.Count++;
            createdAccounts[key] = entry;
            return true;
        }
    }


    public int GetRateLimitDelay(Player player)
    {
        if (!Enabled)
            return 0;
        lock (rateLimitLock)
        {
            var curT = time.ElapsedMilliseconds;
            var t = rateLimits.GetValueOrDefault(player.Key, 0L);
            var delay = (t - curT);
            rateLimits[player.Key] = time.ElapsedMilliseconds + LNetConst.AccountRateLimitMs;
            return (int)(delay > 0 ? delay : 0);
        }
    }

    void CleanWindows(long t)
    {
        lock (windowLock)
        {
            var expired = new List<IPKey>();

            foreach (var pair in createdAccounts)
            {
                if (t - pair.Value.WindowStart >= LNetConst.AccountCreationWindowMs)
                    expired.Add(pair.Key);
            }

            foreach (var key in expired)
                createdAccounts.Remove(key);
        }
    }

    void CleanRateDelay(long t)
    {
        lock (rateLimitLock)
        {
            var expired = new List<IPKey>();
            foreach (var pair in rateLimits)
            {
                if (pair.Value < t)
                    expired.Add(pair.Key);
            }
            foreach (var key in expired)
                rateLimits.Remove(key);
        }
    }

    public void Update()
    {
        var cleanT = time.ElapsedMilliseconds;
        if (cleanT - lastRateLimitClean > 5000)
        {
            Task.Run(() => CleanRateDelay(cleanT));
            lastRateLimitClean = cleanT;
        }

        if (cleanT - lastWindowRateLimitClean > 180000)
        {
            Task.Run(() => CleanWindows(cleanT));
            lastWindowRateLimitClean = cleanT;
        }
    }
}
