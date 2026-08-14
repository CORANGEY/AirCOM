using AirCOM.Server;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        // Port from args[0] or AIRCOM_SERVER_PORT env or default 51000.
        int port = 51000;
        if (args.Length > 0 && int.TryParse(args[0], out var p)) port = p;
        else if (int.TryParse(Environment.GetEnvironmentVariable("AIRCOM_SERVER_PORT"), out var ep)) port = ep;

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine($"AirCOM Server (信令 + 中继) - port {port}");
        Console.WriteLine("Ctrl+C 停止。");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var server = new SignalRelayServer(port);
        try
        {
            await server.RunAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
        Console.WriteLine("已停止。");
    }
}
