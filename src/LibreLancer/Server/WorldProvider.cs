using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using LibreLancer.Data.GameData;
using LibreLancer.Data.GameData.World;

namespace LibreLancer.Server;

public class WorldProvider
{
    private GameServer server;

    public WorldProvider(GameServer server)
    {
        this.server = server;
    }

    public void RemoveWorld(StarSystem system)
    {
        worlds.TryRemove(system, out _);
    }

    private struct WorldState
    {
        public bool Ready;
        public ServerWorld World;
    }

    private ConcurrentDictionary<StarSystem, WorldState> worlds = new();

    private readonly object closeLock = new();
    private volatile bool closed;

    // Finishes each world. After this, RequestWorld gives no world and LoadWorld makes none.
    public void Close()
    {
        lock (closeLock)
        {
            if (closed) return;
            closed = true;
            foreach (var ws in worlds.Values) ws.World?.Finish();
            worlds.Clear();
        }
    }

    private void LoadWorld(StarSystem system, out WorldState ws, PreloadObject[] preloads)
    {
        var x = new WorldState();
        if (!closed && worlds.TryAdd(system, new WorldState()))
        {
            x.World = new ServerWorld(system, server);
            server.GameData.PreloadObjects(preloads, server.Resources);
            lock (closeLock)
            {
                // Close came during the load and did not see this world. It finishes here and is not ready.
                if (closed)
                {
                    x.World.Finish();
                    ws = x;
                    return;
                }
                x.Ready = true;
                worlds.AddOrUpdate(
                    system,
                    _ => x,
                    (_, _) => x
                );
            }
            server.WorldReady(x.World);
        }
        ws = x;
    }
    public void RequestWorld(StarSystem system, Action<ServerWorld> spunUp, PreloadObject[] preloads)
    {
        Task.Run(async () =>
        {
            if (!worlds.TryGetValue(system, out var ws))
                LoadWorld(system, out ws, preloads);
            while (!ws.Ready)
            {
                if (closed)
                    return;
                await Task.Delay(33);
                if (!worlds.TryGetValue(system, out ws))
                    LoadWorld(system, out ws, preloads);
            }

            spunUp(ws.World);
        }).ContinueWith(x =>
        {
            if (x.Exception != null)
                throw x.Exception;
        });
    }
}
