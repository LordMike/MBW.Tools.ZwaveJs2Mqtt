namespace ZwaveMqttTemplater.Z2M.Models;

internal class Z2MNodeGroup
{
    public string text { get; set; }
    public int endpoint { get; set; }
    public int value { get; set; }
    public int maxNodes { get; set; }
    public bool isLifeline { get; set; }
    public bool multiChannel { get; set; }
}