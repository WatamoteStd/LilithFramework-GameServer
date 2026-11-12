using GameWorld;
using Internal;
using Lobby;
using Network;

ushort server_port = ushort.Parse(Environment.GetEnvironmentVariable("SERVER_PORT") ?? "34444");
WinTimer.Begin();

try
{
    Console.WriteLine($"[Moba] Started in defaulth mode.");
    NetworkManager networkManager = new NetworkManager(server_port);
    WorldHolder world = new WorldHolder(networkManager);
    LobbyManager lobby = new LobbyManager(networkManager, world);
    networkManager.Start(world, lobby);

    Console.WriteLine("[Moba] Running. Ctrl+C to stop.");
    Thread.Sleep(Timeout.Infinite);
}
finally
{
    WinTimer.End();
}

