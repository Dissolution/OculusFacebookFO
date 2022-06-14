using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;
using Microsoft.Extensions.Configuration;
using Serilog;
using Console = System.Console;

namespace OculusFacebookFO;

internal sealed class Scanner : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly HashSet<string> _ignoredButtonNames;
    private readonly HashSet<string> _clickButtonNames;
    private AutomationElement? _documentElement;
    private StructureChangedEventHandlerBase? _reactHandler;

    public Scanner(IConfiguration configuration, ILogger logger)
    {
        _configuration = configuration;
        _logger = logger;
        var ignoredNames = _configuration.GetRequiredSection("IgnoredButtonNames").Get<string[]>();
        _ignoredButtonNames = new(ignoredNames, StringComparer.OrdinalIgnoreCase);
        var clickNames = _configuration.GetRequiredSection("ClickButtonNames").Get<string[]>();
        _clickButtonNames = new(clickNames, StringComparer.OrdinalIgnoreCase);
        // As null until initialize
        _documentElement = null;
        _reactHandler = null;
    }

    public async Task<ExitCode> InitializeAsync(CancellationToken token = default)
    {
        TimeSpan? timeout = _configuration.GetValue<TimeSpan?>("timeout", null);

        // Find the running Oculus app
        string? oculusAppName = _configuration.GetValue<string?>("OculusExeName", null);
        if (string.IsNullOrWhiteSpace(oculusAppName))
        {
            _logger.Fatal("appsettings is missing {Key}", "OculusExeName");
            return ExitCode.BadConfig;
        }

        var oculusAppProcess = FindOculusProcess(oculusAppName);
        if (oculusAppProcess is null)
        {
            _logger.Fatal("Could not find Oculus App Process '{AppName}'", oculusAppName);
            return ExitCode.BadOculusExe;
        }

        using var app = Application.Attach(oculusAppProcess);
        using var automation = new UIA3Automation();

        var oculusWindow = app.GetMainWindow(automation, timeout);
        if (oculusWindow is null)
        {
            _logger.Fatal("Could not find Oculus Main Window in {Timeout}", timeout);
            return ExitCode.BadOculusExe;
        }

        // We are always interacting with the window's main document
        _documentElement =
            oculusWindow.FindFirstChild(f => f.ByControlType(ControlType.Document).And(f.ByName("Oculus")));
        if (_documentElement is null)
        {
            var children = oculusWindow.FindAllChildren();
            var desc = oculusWindow.FindAllDescendants();

            _logger.Fatal("Could not find Oculus main window document");
            return ExitCode.BadOculusExe;
        }

        return ExitCode.Ok;
    }

    public async Task ReactAsync(CancellationToken token = default)
    {
        if (_documentElement is null)
        {
            throw new InvalidOperationException("You must call InitializeAsync first!");
        }

        // Attach our handler
        _reactHandler = _documentElement.RegisterStructureChangedEvent(TreeScope.Children, OnStructureChanged);

        // Fire the first check
        await HandleDocument();

        // Loop until cancelled
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1d), token);
        }
    }

    public async Task<ExitCode> ScanAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            // Scan for issue
            var found = await HandleDocument();

            if (!found)
            {
                // Wait before checking again (cooldown)
                await Task.Delay(TimeSpan.FromSeconds(5d), token);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100d), token);
            }
        }

        return ExitCode.Cancelled;
    }

    private void OnStructureChanged(AutomationElement automationElement, StructureChangeType structureChangeType,
                                    int[] rid)
    {
        _logger.Information("Document Structure Changed: {Element} {ChangeType} {RID}",
                            automationElement,
                            structureChangeType,
                            rid);

        if (structureChangeType == StructureChangeType.ChildAdded)
        {
            _logger.Information("Scanning for new buttons");
            // Fire and forget
            Task.Run(async () =>
            {
                bool found;
                do
                {
                    found = await HandleDocument();
                } while (found);
            });
        }
    }

    private Process? FindOculusProcess(string oculusAppName)
    {
        return Process.GetProcessesByName(oculusAppName)
                      .Where(process =>
                      {
                          // Working with `Process` can be fraught with Exceptions
                          try
                          {
                              return process.MainWindowHandle != IntPtr.Zero &&
                                     !string.IsNullOrWhiteSpace(process.MainWindowTitle);
                          }
                          catch
                          {
                              return false;
                          }
                      })
                      .OneOrDefault();
    }

    private async Task<bool> HandleDocument()
    {
        if (_documentElement is null) throw new InvalidOperationException();

        // We're looking for very specific buttons
        var buttons = _documentElement.FindAllDescendants(f => f.ByControlType(ControlType.Button))
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

                                          return !string.IsNullOrWhiteSpace(name) &&
                                                 !_ignoredButtonNames.Contains(name);
                                      })
                                      .Select(ae => ae.AsButton())
                                      .ToList();

        // If we have none, cooldown
        if (buttons.Count == 0)
        {
            var cooldown = TimeSpan.FromSeconds(0.5d);
            _logger.Debug("No buttons found, cooling down for {Cooldown}", cooldown);
            return false;
        }

        // Process
        foreach (var button in buttons)
        {
            string? name = button!.Name;

            // Some we can just click
            if (_clickButtonNames.Contains(name))
            {
                _logger.Debug("Auto-clicking {ButtonName}", name);
                await button.InvokeAsync();
                continue;
            }

            // Some require more
            if (name == "Continue with Oculus")
            {
                _logger.Debug("Scrolling to enable 'Continue with Oculus'");
                // Look for the last ListItem that is offscreen
                var lastListItem = _documentElement
                                   .FindAllDescendants(f => f.ByControlType(ControlType.ListItem))
                                   .Select(ae => ae?.AsListBoxItem())
                                   .LastOrDefault(item => item is not null && item.IsOffscreen);
                lastListItem?.ScrollIntoView();
                await button.InvokeAsync();
                continue;
            }

            // Ignore this button
            _ignoredButtonNames.Add(name);
        }

        // Scan again immediately
        return true;
    }


    public void Dispose()
    {
        if (_documentElement is not null && _reactHandler is not null)
        {
            _documentElement.FrameworkAutomationElement.UnregisterStructureChangedEventHandler(_reactHandler);
        }
    }
    /*
     * 


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
        
     */
}