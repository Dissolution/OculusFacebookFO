using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Serilog;
using Debug = System.Diagnostics.Debug;

namespace OculusFacebookFO;

public sealed class ButtonScanner
{
    private readonly ILogger _logger;
    private readonly OculusApp _oculusApp;
    private readonly Dictionary<string, ButtonAction> _buttonActions;

    public ButtonScanner(OculusApp oculusApp, IDictionary<string, ButtonAction> initialButtonActions)
    {
        _logger = Log.Logger;
        _oculusApp = oculusApp;
        _buttonActions = new Dictionary<string, ButtonAction>(initialButtonActions, StringComparer.OrdinalIgnoreCase)
        {
            // We already know these should be ignored
            { "Minimize", ButtonAction.Ignore },
            { "Maximize", ButtonAction.Ignore },
            { "Close", ButtonAction.Ignore },
            { "Cancel", ButtonAction.Ignore },
            { "Log in with Facebook", ButtonAction.Ignore }
        };
    }

    public async Task ScanAsync(CancellationToken token = default)
    {
        TimeSpan baseCooldown = TimeSpan.FromMilliseconds(250);     // 1/4 of a second
        TimeSpan maxCooldown = TimeSpan.FromSeconds(1);             // When it pops up, we don't want to have to wait too long
        int misses = 0;
        while (!token.IsCancellationRequested)
        {
            // Scan our main document and handle anything applicable it contains
            var found = ScanAndHandleButtons();
            
            // Did we find at least one button?
            if (found)
            {
                // Reset misses
                misses = 0;
                // Only wait a small amount of time before scanning again, as we want to handle everything as fast as possible
                await Task.Delay(baseCooldown, token);
            }
            else // We didn't find any buttons to handle
            {
                // Add to misses
                misses++;
                // Slow us down as we get more misses, until we are coasting
                TimeSpan cooldown = baseCooldown * misses;
                if (cooldown > maxCooldown)
                    cooldown = maxCooldown;
                await Task.Delay(cooldown, token);
            }
        }
        token.ThrowIfCancellationRequested();
    }

    private bool ScanAndHandleButtons()
    {
        var doc = _oculusApp.MainDocument;
        
        // Find all descendant buttons
        var buttons = doc.FindAllDescendants(f => f.ByControlType(ControlType.Button))
                         // Only specific ones
                         .Where(ae =>
                         {
                             // Has to support the Name property
                             if (ae.Properties.Name.TryGetValue(out string? name))
                             {
                                 // Ignore anything with an invalid name
                                 if (string.IsNullOrWhiteSpace(name)) return false;
                                 // Check cache for ignores
                                 if (_buttonActions.TryGetValue(name, out var handle) && 
                                     handle == ButtonAction.Ignore) return false;
                                 return true;
                             }
                             return false;
                         })
                         // As Button
                         .Select(ae => ae.AsButton())
                         .ToList();

        if (buttons.Count == 0)
        {
            _logger.Debug("{Scanner}: No buttons found", this);
            return false;
        }
        
        _logger.Debug("{Scanner}: Found {Count} Buttons: {Buttons}", this, buttons.Count, buttons);
        
        // Process everything we've found
        foreach (Button button in buttons)
        {
            string name = button.Name;
            // Do we have a registered button action?
            if (_buttonActions.TryGetValue(name, out var action))
            {
                Log.Logger.Debug("{Scanner}: Button '{ButtonName}' being {ButtonAction}ed", this, name, action);
                switch (action)
                {
                    case ButtonAction.Ignore:
                        continue;
                    case ButtonAction.Click:
                        try
                        {
                            button.Invoke();
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "{Scanner}: Could not Invoke {ButtonName}", this, name);
                        }
                        continue;
                    case ButtonAction.ScrollClick:
                        try
                        {
                            // Look for the last ListItem that is offscreen
                            var lastListItem = doc.FindAllDescendants(f => f.ByControlType(ControlType.ListItem))
                                                  .Where(ae => ae.Properties.IsOffscreen.IsSupported &&
                                                               ae.Patterns.ScrollItem.IsSupported)
                                                  .LastOrDefault(item => item.IsOffscreen);
                            if (lastListItem is null)
                            {
                                _logger.Warning("{Scanner}: Could not find an offscreen item to scroll to", this);
                            }
                            else
                            {
                                var scrollItemPattern = lastListItem.Patterns.ScrollItem;
                                Debug.Assert(scrollItemPattern.IsSupported);
                                // Scroll down to it (to the bottom)
                                scrollItemPattern.Pattern.ScrollIntoView();
                            }
                            
                            // Now we can click the button
                            button.Invoke();
                        }
                        catch (Exception ex)
                        {
                            _logger.Debug(ex, "{Scanner}: Could not ScrollClick {ButtonName}", this, name);
                        }
                        continue;
                    default:
                    {
                        _logger.Error("{Scanner}: Unknown ButtonAction '{ButtonAction}'", this, action);
                        break;
                    }
                }
            }

            // We either had no action or the ButtonAction was bad
            // In either case, log it and then ignore it forever
            _logger.Debug("{Scanner}: Button '{ButtonName}' now being ignored", this, name);
            _buttonActions[name] = ButtonAction.Ignore;
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => "ButtonScanner";
}