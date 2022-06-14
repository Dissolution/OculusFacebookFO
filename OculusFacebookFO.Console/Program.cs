using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core;
using FlaUI.UIA3;
using Microsoft.Extensions.Configuration;
using OculusFacebookFO;
using OculusFacebookFO.Console;
using OculusFacebookFO.Controls;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace OculusFacebookFO.Console;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
                     .MinimumLevel.Is(LogEventLevel.Verbose)
                     .WriteTo.Console(theme: SystemConsoleTheme.Colored)
                     .CreateLogger();
        try
        {
            using var cts = new CancellationTokenSource();
            var config = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.json")
                         .Build();

         
        }
        catch (Exception ex)
        {
            Debugger.Break();
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }




        async Task<TimeSpan> HandleDocument(AutomationElement document)
        {
            var ignoreButtonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Minimize",
                "Maximize",
                "Close",
            };
            var clickButtonNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Continue",
                "Confirm",
            };
    
            // We're looking for very specific buttons
            var buttons = document.FindAllDescendants(f => f.ByControlType(ControlType.Button))
                                  .Where(ae =>
                                  {
                                      if (ae is null) return false;
                                      string? name;
                                      try
                                      {
                                          name = ae.Name;
                                      }
                                      catch
                                      {
                                          return false;
                                      }
                                      return !ignoreButtonNames.Contains(name);
                                  })
                                  .Select(ae => ae.AsButton())
                                  .ToList();
    
            // If we have none, cooldown
            if (buttons.Count == 0)
            {
                return TimeSpan.FromSeconds(5d);
            }
    
            // Process
            foreach (var button in buttons)
            {
                string? name = button!.Name;
                // Some we can just click
                if (clickButtonNames.Contains(name))
                {
                    await button.InvokeAsync();
                    continue;
                }
        
                // Some require more
                if (name == "Continue with Oculus")
                {
                    // Look for the last ListItem that is offscreen
                    var lastListItem = document
                                       .FindAllDescendants(f => f. ByControlType(ControlType.ListItem))
                                       .Select(ae => ae?.AsListBoxItem())
                                       .LastOrDefault(item => item is not null && item.IsOffscreen);
                    lastListItem?.ScrollIntoView();

                    await button.InvokeAsync();
                    continue;
                }

                // Ignore this button
            }
   
            // Scan again immediately
            return TimeSpan.FromMilliseconds(100d);
        }

        bool IsImportantUpdateDialog(AutomationElement? automationElement)
        {
            if (automationElement is null) return false;
            // Has to have one Document
            var documentElement = automationElement.FindFirstChild(f => f.ByControlType(ControlType.Document));
            if (documentElement is null) return false;
            // Has to have text
            var textElement = documentElement.FindFirstChild(f => f.ByControlType(ControlType.Text));
            if (textElement is null) return false;
            // Text has to be 
            string dialogMatchText = config.GetValue<string>("ImportantUpdateDialogText");
            return string.Equals(textElement.Name, dialogMatchText);
        }

        AutomationElement? FindImportantUpdateDialog(Window oculusWindow)
        {
            try
            {
                return oculusWindow.FindAllChildren(f => f.ByLocalizedControlType("dialog"))
                                   .Where(IsImportantUpdateDialog)
                                   .OneOrDefault();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return null;
            }
        }


        
    }
}


internal sealed class Scanner
{
    private readonly IConfiguration _configuration;

    public Scanner(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task ScanAsync(CancellationToken token = default)
    {
           TimeSpan? timeout = config.GetValue<TimeSpan?>("timeout", null);

            // Find the running Oculus app
            string? oculusAppName = config.GetValue<string?>("OculusExeName", null);
            if (string.IsNullOrWhiteSpace(oculusAppName))
            {
                Console.WriteLine("appsettings is missing 'OculusExeName'");
                return (int)ExitCode.BadConfig;
            }

            var oculusAppProcess = FindOculusProcess(oculusAppName);
            if (oculusAppProcess is null)
            {
                Console.WriteLine($"Could not find {oculusAppName}");
                return (int)ExitCode.BadOculusExe;
            }

            // Find Oculus app window
            string? oculusWindowName = config.GetValue<string?>("OculusWindowName", null);
            if (string.IsNullOrWhiteSpace(oculusWindowName))
            {
                Console.WriteLine("appsettings is missing 'OculusWindowName'");
                return (int)ExitCode.BadConfig;
            }

            using var app = Application.Attach(oculusAppProcess);
            using var automation = new UIA3Automation();

            var oculusWindow = app.GetMainWindow(automation, timeout);
            if (oculusWindow is null)
            {
                Console.WriteLine($"Could not find Oculus Main Window '{oculusWindowName}' in {timeout}");
                return (int)ExitCode.BadOculusExe;
            }

            // We are always interacting with the window's main document
            var mainDocument = oculusWindow.FindFirstChild(f => f.ByControlType(ControlType.Document).And(f.ByName("Oculus")));
            if (mainDocument is null)
            {
                Console.WriteLine($"Could not find Oculus main window document");
                return (int)ExitCode.BadOculusExe;
            }

            // Scan
            Console.WriteLine("Press Esc at any time to kill this process.");

            while (true)
            {
                // Check for Escape pressed
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        break;
                    }
                }

                // Scan for issue
                var cooldown = await HandleDocument(mainDocument);

                // Wait before checking again (cooldown)
                await Task.Delay(cooldown, cts.Token);
            }

            Console.WriteLine("Closing...");
            cts.Cancel();
            return 0;
    }
}



