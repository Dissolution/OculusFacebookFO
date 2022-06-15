using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.EventHandlers;
using Serilog;
using Debug = System.Diagnostics.Debug;

namespace OculusFacebookFO;

public sealed class OculusApp : IDisposable
{
    public static async Task<OculusApp> CreateAsync(string oculusAppPath)
    {
        // Find the running Oculus Process
        var oculusAppProcess = await AttachAsync(oculusAppPath);
        // Setup FLAUI -- passed on or we dispose
        var app = Application.Attach(oculusAppProcess);
        // Try to acquire the main window
        var mainWindow = app.GetMainWindow(Program.Automation, TimeSpan.FromSeconds(30));
        if (mainWindow is null)
            throw new OculusApplicationException($"Could not find Oculus {app} Main Window");
        // Try to acquire the main document
        var mainDocument = await GetMainDocumentAsync(mainWindow);
        // Now we can create the app
        return new OculusApp(oculusAppProcess, app, mainWindow, mainDocument);
    }

    private static Task<Process> AttachAsync(string oculusAppPath)
    {
        if (!File.Exists(oculusAppPath))
            throw new OculusApplicationException($"Invalid Oculus Application Path '{oculusAppPath}'");
        // Process name will be file name without extension
        var processName = Path.GetFileNameWithoutExtension(oculusAppPath);

        // Look for valid Process
        var process = Process.GetProcessesByName(processName)
                               .Where(IsValidProcess)
                               .OneOrDefault();
        if (process is null)
            throw new OculusApplicationException($"Oculus Application is not running");
        process.WaitForInputIdle();
        return Task.FromResult(process);
    }

    private static async Task<Process> AttachOrLaunchAsync(string oculusAppPath)
    {
        if (!File.Exists(oculusAppPath))
            throw new OculusApplicationException($"Invalid Oculus Application Path '{oculusAppPath}'");
        // Process name will be file name without extension
        var processName = Path.GetFileNameWithoutExtension(oculusAppPath);

        // Look for valid Processes with this name
        var processes = Process.GetProcessesByName(processName)
                               .Where(IsValidProcess)
                               .ToList();

        var process = processes.OneOrDefault();
        // None, we have to launch
        if (process is null)
        {
            process = Process.Start(oculusAppPath);
            await Task.Delay(TimeSpan.FromSeconds(5));
           
            if (process is null || process.HasExited)
                throw new OculusApplicationException($"Could not launch the Oculus Application at '{oculusAppPath}'");
            // Wait until it's ready
            process.WaitForInputIdle((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
            while (!IsValidProcess(process))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
            //process.WaitForInputIdle((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        }

        Debug.Assert(process is not null);
        Debug.Assert(IsValidProcess(process));
        return process;
    }

    private static bool IsValidProcess(Process process)
    {
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

    private static async Task<AutomationElement> GetMainDocumentAsync(Window mainWindow)
    {
        // We might have to wait a bit while things shuffle, so be gentle
        AutomationElement? docElement;
        while (true)
        {
            docElement = mainWindow.FindFirstChild(cf => cf.ByControlType(ControlType.Document).And(cf.ByName("Oculus")));
            if (docElement is not null) break;
            await Task.Delay(TimeSpan.FromSeconds(0.5d));
        }

        return docElement;
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
                Log.Logger.Debug("Document watching for Structure Changes");
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
        Log.Logger
           .Information("{ChangeType}: {RID}-{Element}",
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
        Log.Logger.Debug("Oculus App Disposed");
    }
}