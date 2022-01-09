namespace ZwaveMqttTemplater.ConfigModels;

internal class DesiredAssociations
{
    /// <summary>
    /// GroupRef => TargetRef
    /// </summary>
    public Dictionary<string, HashSet<string>> Links { get; set; } = new();
}