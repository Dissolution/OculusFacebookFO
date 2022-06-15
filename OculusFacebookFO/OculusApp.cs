using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using Serilog;

namespace OculusFacebookFO;

public sealed class OculusApp : IDisposable
{
    private static readonly ILogger _logger = Log.Logger;

    public static async Task<OculusApp> CreateAsync(string oculusAppPath, CancellationToken token = default)
    {
        // Wait / Attach to a running Oculus App Process
        var oculusAppName = Path.GetFileNameWithoutExtension(oculusAppPath);
        var oculusAppProcess = await WaitAttachAsync(oculusAppName, token);
        token.ThrowIfCancellationRequested();
        
        // Setup FLAUI -- passed on or we dispose
        var app = Application.Attach(oculusAppProcess);
        token.ThrowIfCancellationRequested();
        
        // Try to acquire the main window
        var timeout = TimeSpan.FromSeconds(30);
        var mainWindow = app.GetMainWindow(Program.Automation, timeout);
        if (mainWindow is null)
            throw new OculusApplicationException($"Could not find Oculus {app} Main Window in {timeout}");
        token.ThrowIfCancellationRequested();
        
        // Try to acquire the main document
        var mainDocument = await GetMainDocumentAsync(mainWindow);
        token.ThrowIfCancellationRequested();
        
        // Now we can create the app
        return new OculusApp(oculusAppProcess, app, mainWindow, mainDocument);
    }

    private static async Task<Process> WaitAttachAsync(string oculusAppName, CancellationToken token = default)
    {
        while (!token.IsCancellationRequested)
        {
            // Try to find a single matching process
            var process = Process.GetProcessesByName(oculusAppName)
                                 .Where(IsValidProcess)
                                 .OneOrDefault();
            if (process is not null)
            {
                _logger.Debug("Found Oculus App Process: {Process}", process);   
                return process;
            }
            
            // Cooldown
            var cooldown = TimeSpan.FromSeconds(5);
            _logger.Debug("Did not find Oculus App Process, waiting {Cooldown}", cooldown);
            await Task.Delay(cooldown, token);
        }
        token.ThrowIfCancellationRequested();
        throw new TaskCanceledException();
    }

    /// <summary>
    /// Determines whether the given <paramref name="process"/> is one that we can interact with
    /// </summary>
    private static bool IsValidProcess([NotNullWhen(true)] Process? process)
    {
        if (process is null) return false;
        try
        {
            // Has not exited
            return !process.HasExited &&
                   // Is responding
                   process.Responding &&
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
    }

    /// <summary>
    /// Gets the main document <see cref="AutomationElement"/> for the given <paramref name="mainWindow"/>
    /// </summary>
    private static async Task<AutomationElement> GetMainDocumentAsync(Window mainWindow, CancellationToken token = default)
    {
        // We might have to wait a bit while things shuffle, so be gentle
        AutomationElement? docElement = null;
        while (!token.IsCancellationRequested)
        {
            docElement = mainWindow.FindFirstChild(cf => cf.ByControlType(ControlType.Document).And(cf.ByName("Oculus")));
            if (docElement is not null)
            {
                _logger.Debug("{MainWindow} found {MainDocument}", mainWindow, docElement);
                break;
            }
            var cooldown = TimeSpan.FromSeconds(0.5);
            _logger.Debug("{MainWindow} could not find main document, waiting {Cooldown}", mainWindow, cooldown);
            await Task.Delay(cooldown, token);
        }
        token.ThrowIfCancellationRequested();
        return docElement!;
    }

    private readonly Process _process;
    private readonly Application _application;
    private readonly Window _mainWindow;
    private readonly AutomationElement _mainDocument;

    private StructureChanged? _structureChanged;
    private StructureChangedEventHandlerBase? _structureChangedEventHandler;

    public Window MainWindow => _mainWindow;
    public AutomationElement MainDocument => _mainDocument;

    public event StructureChanged? StructureChanged
    {
        add
        {
            if (_structureChangedEventHandler is null)
            {
                _structureChangedEventHandler =
                    _mainDocument.RegisterStructureChangedEvent(TreeScope.Subtree, OnStructureChanged);
                _logger.Debug("Now watching {MainDocument} for Structure Changes", _mainDocument);
            }
            _structureChanged += value;
        }
        remove => _structureChanged -= value;
    }

    private OculusApp(Process process, Application application, Window mainWindow, AutomationElement mainDocument)
    {
        _process = process;
        _application = application;
        _mainWindow = mainWindow;
        _mainDocument = mainDocument;
    }
    
    private void OnStructureChanged(AutomationElement automationElement,
                                    StructureChangeType structureChangeType,
                                    int[] rid)
    {
        _logger.Information("{ChangeType}: {RID}-{Element}",
                            structureChangeType, rid, automationElement);
        _structureChanged?.Invoke(automationElement, structureChangeType, rid);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_structureChangedEventHandler is not null)
        {
            _mainDocument.FrameworkAutomationElement
                         .UnregisterStructureChangedEventHandler(_structureChangedEventHandler);
        }
        _application.Dispose();
        _process.Dispose();
        _logger.Debug("Oculus App Disposed");
    }
}