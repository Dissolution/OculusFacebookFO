using System.Configuration;
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using FlaUI.UIA3;
using Microsoft.Extensions.Configuration;
using Serilog;
using Debug = System.Diagnostics.Debug;

namespace OculusFacebookFO;

internal sealed class Scanner : IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly HashSet<string> _ignoredButtonNames;
    private readonly HashSet<string> _clickButtonNames;
    private readonly string _lastButtonName;
    
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
        _lastButtonName = _configuration.GetRequiredSection("LastButtonName").Get<string>();
        // As null until initialize
        _documentElement = null;
        _reactHandler = null;
    }

    public async Task InitializeAsync(CancellationToken token = default)
    {
        // Find the running Oculus Process
        var oculusAppProcess = FindOculusProcess();
        if (oculusAppProcess is null)
        {
            throw new OculusApplicationException("Could not find running Oculus Application Process");
        }

        // Setup FLAUI
        using var app = Application.Attach(oculusAppProcess);
        using var automation = new UIA3Automation();

        // Acquire the Main Window
        var oculusWindow = app.GetMainWindow(automation, TimeSpan.FromSeconds(10));
        if (oculusWindow is null)
        {
            throw new OculusApplicationException($"Could not find Oculus {app} Main Window");
        }

        // We are interacting with the window's main document
        _documentElement = oculusWindow.FindFirstChild(f => f.ByControlType(ControlType.Document).And(f.ByName("Oculus")));
        // Sometimes it takes a bit to be found
        while (_documentElement is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            _documentElement = oculusWindow.FindFirstChild(f => f.ByControlType(ControlType.Document).And(f.ByName("Oculus")));
            //
            //
            // var children = oculusWindow.FindAllChildren();
            // var desc = oculusWindow.FindAllDescendants();
            //
            // throw new OculusApplicationException($"Could not find Oculus {app} {oculusWindow} Main Document");
        }
    }

    /*public async Task ReactAsync(CancellationToken token = default)
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
    }*/

    public async Task ScanAsync(CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            // Scan our main document and handle anything applicable it contains
            var result = await ScanDocumentAsync();

            if (result == ScanResult.NothingFound)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
            else if (result == ScanResult.InProcess)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }
            else if (result == ScanResult.Ended)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
        }
        token.ThrowIfCancellationRequested();
    }

    private Process? FindOculusProcess()
    {
        string? oculusProcessName = _configuration.GetValue<string?>("OculusProcessName", null);
        if (string.IsNullOrWhiteSpace(oculusProcessName))
        {
            throw new InvalidOperationException($"{_configuration} is missing key '{"OculusProcessName"}'");
        }

        // There should only be a single valid process with this name
        return Process.GetProcesses()
                      .Where(process =>
                      {
                          try
                          {
                              // Matches process name
                              return string.Equals(process.ProcessName, oculusProcessName) &&
                                     // Has a valid main window Handle
                                     process.MainWindowHandle != IntPtr.Zero &&
                                     // Has a valid main window title
                                     !string.IsNullOrWhiteSpace(process.MainWindowTitle);
                          }
                          // Process likes to throw Exceptions
                          catch
                          {
                              return false;
                          }
                      })
                      .OneOrDefault();
    }
    
    //
    // private void OnStructureChanged(AutomationElement automationElement, StructureChangeType structureChangeType,
    //                                 int[] rid)
    // {
    //     _logger.Information("Document Structure Changed: {Element} {ChangeType} {RID}",
    //                         automationElement,
    //                         structureChangeType,
    //                         rid);
    //
    //     if (structureChangeType == StructureChangeType.ChildAdded)
    //     {
    //         _logger.Information("Scanning for new buttons");
    //         // Fire and forget
    //         Task.Run(async () =>
    //         {
    //             bool found;
    //             do
    //             {
    //                 found = await ScanDocumentAsync();
    //             } while (found);
    //         });
    //     }
    // }

   

    private async Task<ScanResult> ScanDocumentAsync()
    {
        Debug.Assert(_documentElement is not null);
        
        // Find all descendant buttons
        var buttons = _documentElement.FindAllDescendants(f => f.ByControlType(ControlType.Button))
                                      // Only specific ones
                                      .Where(ae =>
                                      {
                                          // Some AutomationElements do not support the Name property
                                          var nameProperty = ae.Properties.Name;
                                          if (!nameProperty.IsSupported) return false;
                                          string name = nameProperty.Value;
                                          // Ignore any with bad names            
                                          return !string.IsNullOrWhiteSpace(name) &&
                                                 !_ignoredButtonNames.Contains(name);
                                      })
                                      // As Button
                                      .Select(ae => ae.AsButton())
                                      .ToList();

        // None?
        if (buttons.Count == 0)
        {
            _logger.Debug("No buttons found");
            return ScanResult.NothingFound;
        }

        // Process
        var scanResult = ScanResult.InProcess;
        foreach (Button button in buttons)
        {
            string? name = button.Name;

            // One button we know is last, which lets us cooldown
            if (string.Equals(name, _lastButtonName))
            {
                _logger.Debug("Clicking {ButtonName}: end-of-sequence", name);
                await button.InvokeAsync();
                scanResult = ScanResult.Ended;
            }
            // Some we can just click
            else if (_clickButtonNames.Contains(name))
            {
                _logger.Debug("Auto-clicking {ButtonName}", name);
                await button.InvokeAsync();
            }
            // Some require more
            else if (name == "Continue with Oculus")
            {
                _logger.Debug("Scrolling to enable 'Continue with Oculus'");
                // Look for the last ListItem that is offscreen
                var lastListItem = _documentElement
                                   .FindAllDescendants(f => f.ByControlType(ControlType.ListItem))
                                   .Where(ae => ae.Properties.IsOffscreen.IsSupported)
                                   .Select(ae => ae.AsListBoxItem())
                                   .LastOrDefault(item => item is not null && item.IsOffscreen);
                // Scroll down to it (to the bottom)
                lastListItem?.ScrollIntoView();
                // Now we can click the button
                await button.InvokeAsync();
            }
            else
            {
                // Permanently ignore this button
                _logger.Debug("Permanently ignoring {ButtonName}", name);
                _ignoredButtonNames.Add(name);
            }
        }

        return scanResult;
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