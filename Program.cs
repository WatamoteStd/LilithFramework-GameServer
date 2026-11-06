ushort server_port = ushort.Parse(Environment.GetEnvironmentVariable("SERVER_PORT") ?? "34444");

Console.WriteLine($"[Moba] Started in hiden mode.");

Thread.Sleep(Timeout.Infinite);