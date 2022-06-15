using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace OculusFacebookFO;

/// <summary>
/// An event delegate representing when an <see cref="AutomationElement"/>'s structure changes
/// </summary>
/// <param name="automationElement">
/// The <see cref="AutomationElement"/> that changed
/// </param>
/// <param name="structureChangeType">
/// The type of structure change the <paramref name="automationElement"/> had
/// </param>
/// <param name="runtimeId">
/// The unique Runtime Identifier for the <paramref name="automationElement"/>
/// </param>
public delegate void StructureChanged(AutomationElement automationElement, 
                                      StructureChangeType structureChangeType,
                                      int[] runtimeId);