namespace ZwaveMqttTemplater.Helpers;

internal static class Extensions
{
    public delegate bool Predicate<TKey, TValue>(TKey key, TValue value);

    public static void RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> dict, IEnumerable<TKey> keys)
    {
        foreach (TKey key in keys)
            dict.Remove(key);
    }

    public static void RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> dict, Predicate<TKey, TValue> predicate)
    {
        List<TKey> toRemove = dict.Where(entry => predicate(entry.Key, entry.Value)).Select(s => s.Key).ToList();
        dict.RemoveAll(toRemove);
    }

    public static void RemoveAll<TKey>(this ISet<TKey> set, IEnumerable<TKey> keys)
    {
        foreach (TKey key in keys)
            set.Remove(key);
    }

    public static void RemoveAll<TKey>(this ISet<TKey> set, Predicate<TKey> predicate)
    {
        List<TKey> toRemove = set.Where(entry => predicate(entry)).ToList();
        set.RemoveAll(toRemove);
    }

    public static void RemoveAll<TItem>(this IList<TItem> list, Predicate<TItem> predicate)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (predicate(list[i]))
                list.RemoveAt(i);
        }
    }
}