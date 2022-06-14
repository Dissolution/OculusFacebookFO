using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace OculusFacebookFO;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var logger = Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Is(LogEventLevel.Verbose)
                     .WriteTo.Console(theme: SystemConsoleTheme.Colored)
                     .CreateLogger();
        try
        {
            // Scan
            Console.WriteLine("Press Esc at any time to kill this process.");
            
            using var cts = new CancellationTokenSource();
            var config = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.json")
                         .Build();

            using var scanner = new Scanner(config, Log.Logger);
            await scanner.InitializeAsync(cts.Token);
            
            //await scanner.ScanAsync(cts.Token);

            var scannerTask = scanner.ScanAsync(cts.Token);
            //var scannerTask = scanner.ReactAsync(cts.Token);
            var escTask = WaitForEscAsync(cts.Token);
            var completed = await Task.WhenAny(scannerTask, escTask);
            Debugger.Break();
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Unhandled application exception");
            Debugger.Break();
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
        
        // Finished okay!
        return 0;
    }

    private static async Task WaitForEscAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            // Check for Escape pressed
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    return;
                }
            }
            
            // Cooldown
            await Task.Delay(TimeSpan.FromSeconds(0.5d), token);
        }
    }
}