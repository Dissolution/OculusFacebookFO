using FlaUI.Core.AutomationElements;

namespace OculusFacebookFO;

/// <summary>
/// An action to be performed on a <see cref="Button"/>
/// </summary>
public enum ButtonAction
{
    /// <summary>
    /// Do nothing
    /// </summary>
    Ignore,
    /// <summary>
    /// Click/Invoke
    /// </summary>
    Click,
    /// <summary>
    /// Scroll down, then click/invoke
    /// </summary>
    ScrollClick,
}