using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Serilog;

namespace OculusFacebookFO;

public class SequenceScanner
{
    private readonly OculusApp _oculusApp;
    private readonly Dictionary<string, HandleButton> _sequence;

    public SequenceScanner(OculusApp oculusApp, Dictionary<string, HandleButton> sequence)
    {
        _oculusApp = oculusApp;
        _sequence = new Dictionary<string, HandleButton>(sequence, StringComparer.OrdinalIgnoreCase)
        {
            { "Minimize", HandleButton.Ignore },
            { "Maximize", HandleButton.Ignore },
            { "Close", HandleButton.Ignore },
            { "Cancel", HandleButton.Ignore },
            { "Log in with Facebook", HandleButton.Ignore }
        };
    }

    public async Task ScanAsync(CancellationToken token = default)
    {
        TimeSpan aggressiveCooldown = TimeSpan.FromMilliseconds(100);
        TimeSpan passiveCooldown = TimeSpan.FromSeconds(5);
        int misses = 0;

        while (!token.IsCancellationRequested)
        {
            // Scan our main document and handle anything applicable it contains
            var found = HandleSequence();
            if (found)
            {
                misses = 0;
                await Task.Delay(aggressiveCooldown, token);
            }
            else
            {
                misses++;
                if (misses < 25)
                {
                    await Task.Delay(aggressiveCooldown, token);
                }
                else
                {
                    await Task.Delay(passiveCooldown, token);
                }
            }
        }
        token.ThrowIfCancellationRequested();
    }

    private bool HandleSequence()
    {
        var doc = _oculusApp.MainDocument;
        
        // Find all descendant buttons
        var buttons = doc.FindAllDescendants(f => f.ByControlType(ControlType.Button))
                         // Only specific ones
                         .Where(ae =>
                         {
                             // Some AutomationElements do not support the Name property
                             if (ae.Properties.Name.TryGetValue(out string? name))
                             {
                                 // Ignore anything with an invalid name
                                 if (string.IsNullOrWhiteSpace(name)) return false;
                                 // Check cache for ignores
                                 if (_sequence.TryGetValue(name, out var handle))
                                 {
                                     if (handle == HandleButton.Ignore) return false;
                                 }
                                 return true;
                             }
                             return false;
                         })
                         // As Button
                         .Select(ae => ae.AsButton())
                         .ToList();

        if (buttons.Count == 0)
            return false;
        
        foreach (var button in buttons)
        {
            string name = button.Name;
            if (_sequence.TryGetValue(name, out var action))
            {
                Log.Logger.Debug("Button '{ButtonName}' being {ButtonAction}ed", name, action);
                if (action == HandleButton.Ignore)
                {
                    Debugger.Break();
                    continue;
                }

                if (action == HandleButton.Click)
                {
                    try
                    {
                        button.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Debug(ex, "{ButtonName}: Could not Invoke", name);
                    }
                    continue;
                }

                if (action == HandleButton.ScrollClick)
                {
                    try
                    {
                        // Look for the last ListItem that is offscreen
                        var lastListItem = doc.FindAllDescendants(f => f.ByControlType(ControlType.ListItem))
                                              .Where(ae => ae.Properties.IsOffscreen.IsSupported)
                                              .Select(ae => ae.AsListBoxItem())
                                              .LastOrDefault(item => item is not null && item.IsOffscreen);
                        // Scroll down to it (to the bottom)
                        lastListItem?.ScrollIntoView();
                        // Now we can click the button
                        button.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Debug(ex, "{ButtonName}: Could not ScrollClick", name);
                    }
                    continue;
                }
            }
        }

        return true;
    }
    

}