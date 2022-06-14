using System.Diagnostics.CodeAnalysis;

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
    
    [return: NotNullIfNotNull("default")]
    public static T? OneOrDefault<T>(this IEnumerable<T> enumerable, T? @default = default)
    {
        ArgumentNullException.ThrowIfNull(enumerable);
        using var e = enumerable.GetEnumerator();
        if (!e.MoveNext()) return @default;
        var one = e.Current;
        if (e.MoveNext()) return @default;
        return one;
    }
}