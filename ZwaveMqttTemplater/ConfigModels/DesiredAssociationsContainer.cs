namespace ZwaveMqttTemplater.ConfigModels;

internal class DesiredAssociationsContainer
{
    public Dictionary<string, DesiredAssociations> Nodes { get; set; } = new();
}