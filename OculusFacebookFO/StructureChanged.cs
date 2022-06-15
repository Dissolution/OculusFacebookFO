using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace OculusFacebookFO;

public delegate void StructureChanged(AutomationElement automationElement, StructureChangeType structureChangeType,
                                      int[] runtimeId);