using FlaUI.Core.AutomationElements;

namespace OculusFacebookFO.Controls;

public static class AutomationElementExtensions
{
    public static async Task<TElement> WaitUntilClickableAsync<TElement>(this TElement element,
                                                                         TimeSpan? timeout = null,
                                                                         CancellationToken token = default)
        where TElement : AutomationElement
    {
        while (!token.IsCancellationRequested)
        {
            if (element.TryGetClickablePoint(out _))
                return element;
            await Task.Delay(timeout ?? TimeSpan.Zero, token);
        }
        token.ThrowIfCancellationRequested();
        throw new TaskCanceledException();
    }

    public static async Task<Button> InvokeAsync(this Button button,
                                                 TimeSpan? timeout = null,
                                                 CancellationToken token = default)
    {
        while (!token.IsCancellationRequested && !button.IsEnabled)
        {
            await Task.Delay(timeout ?? TimeSpan.Zero, token);
        }
        token.ThrowIfCancellationRequested();
        button.Invoke();
        return button;
    }
}