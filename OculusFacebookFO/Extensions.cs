using System.Diagnostics.CodeAnalysis;
using FlaUI.Core.AutomationElements;
using Microsoft.Extensions.Configuration;

namespace OculusFacebookFO;

public static class Extensions
{
    [return: NotNullIfNotNull("default")]
    public static T? OneOrDefault<T>(this T[] array, T? @default = default)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Length == 1)
            return array[0];
        return @default;
    }
    
    /// <summary>
    /// Returns the only value in this <see cref="IEnumerable{T}"/> or
    /// <paramref name="defaultItem"/> if there is not exactly one value.
    /// </summary>
    /// <typeparam name="T">
    /// The <see cref="Type"/> of values in <paramref name="enumerable"/>
    /// </typeparam>
    /// <param name="enumerable">
    /// The <see cref="IEnumerable{T}"/> to get one value from
    /// </param>
    /// <param name="defaultItem">
    /// The <typeparamref name="T"/> value to return if there is not exactly one value in <paramref name="enumerable"/>
    /// </param>
    /// <returns>
    /// The only value in <paramref name="enumerable"/> or <paramref name="defaultItem"/> if there is not exactly one
    /// </returns>
    [return: NotNullIfNotNull("defaultItem")]
    public static T? OneOrDefault<T>(this IEnumerable<T> enumerable, T? defaultItem = default)
    {
        ArgumentNullException.ThrowIfNull(enumerable);
        using var e = enumerable.GetEnumerator();
        if (!e.MoveNext()) return defaultItem;
        var one = e.Current;
        if (e.MoveNext()) return defaultItem;
        return one;
    }

    public static async Task InvokeAsync(this Button button,
                                         TimeSpan? timeout = null,
                                         CancellationToken token = default)
    {
        while (!token.IsCancellationRequested && !button.IsEnabled)
        {
            await Task.Delay(timeout ?? TimeSpan.Zero, token);
        }
        token.ThrowIfCancellationRequested();
        button.Invoke();
    }

    public static T GetRequiredValue<T>(this IConfiguration configuration, string keyName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(keyName);
        if (!section.Exists())
            throw new InvalidOperationException($"{configuration} is missing key '{keyName}'");
        string? value = section.Value;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{configuration} key '{keyName}' is not a {typeof(T).Name} value", ex);
        }
    }
}