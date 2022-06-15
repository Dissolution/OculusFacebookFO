using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FlaUI.Core;
using FlaUI.UIA3;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace OculusFacebookFO;

internal static class Program
{
    private static AutomationBase? _automation = null;

    public static AutomationBase Automation
    {
        // Will not be null by the time any code calls this
        get => _automation!;
    }
    
    public static async Task<int> Main(string[] args)
    {
        // setup logging
        var logger = Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Is(LogEventLevel.Verbose)
                     .WriteTo.Console(theme: SystemConsoleTheme.Colored)
                     .CreateLogger();
        // setup flaui automation
        _automation = new UIA3Automation();
        try
        {
            // Scan
            Console.WriteLine("Press Esc at any time to kill this process.");
            
            using var cts = new CancellationTokenSource();
            var config = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.json")
                         .Build();

            var oculusAppPath = config.GetValue<string?>("OculusAppPath");
            if (string.IsNullOrWhiteSpace(oculusAppPath))
                throw new OculusApplicationException($"Invalid Oculus Application Path '{oculusAppPath}'");
            using var oculusApp = await OculusApp.CreateAsync(oculusAppPath);

            var sequence = config.GetRequiredSection("Sequence")
                                 .Get<Dictionary<string, HandleButton>>();

            var scanner = new SequenceScanner(oculusApp, sequence);
            
            var escTask = WaitForEscAsync(cts.Token);
            var scannerTask = scanner.ScanAsync(cts.Token);
            var completedTask = await Task.WhenAny(escTask, scannerTask);
            Debugger.Break();

            /*
             using var scanner = new Scanner(config, Log.Logger);
            await scanner.InitializeAsync(cts.Token);
            
            //await scanner.ScanAsync(cts.Token);

            var scannerTask = scanner.ScanAsync(cts.Token);
            //var scannerTask = scanner.ReactAsync(cts.Token);
            var escTask = WaitForEscAsync(cts.Token);
            var completed = await Task.WhenAny(scannerTask, escTask);
            Debugger.Break();
            */
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Unhandled application exception");
            Debugger.Break();
            return 1;
        }
        finally
        {
            _automation.Dispose();
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