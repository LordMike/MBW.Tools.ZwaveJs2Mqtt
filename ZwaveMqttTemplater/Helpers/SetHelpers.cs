namespace ZwaveMqttTemplater.Helpers;

internal static class SetHelpers
{
    public static (TItem[] leftOnly, TItem[] both, TItem[] rightOnly) Compare<TItem>(IList<TItem> left, IList<TItem> right, Func<TItem, string> selector)
    {
        Dictionary<string, TItem> leftSet = left.ToDictionary(selector, StringComparer.Ordinal);
        Dictionary<string, TItem> rightSet = right.ToDictionary(selector, StringComparer.Ordinal);

        IEnumerable<string> leftOnly = leftSet.Keys.Except(rightSet.Keys);
        IEnumerable<string> both = leftSet.Keys.Intersect(rightSet.Keys);
        IEnumerable<string> rightOnly = rightSet.Keys.Except(leftSet.Keys);

        return (leftOnly.Select(x => leftSet[x]).ToArray(), both.Select(x => leftSet[x]).ToArray(), rightOnly.Select(x => rightSet[x]).ToArray());
    }
}