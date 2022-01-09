namespace ZwaveMqttTemplater.Z2M.Models;

internal class Z2MValue
{
    public string id { get; set; }
    public int nodeId { get; set; }
    public int commandClass { get; set; }
    public string commandClassName { get; set; }
    public string property { get; set; }
    public string propertyName { get; set; }
    public string type { get; set; }
    public bool readable { get; set; }
    public bool writeable { get; set; }
    public string description { get; set; }
    public string label { get; set; }
    public int _default { get; set; }
    public bool stateless { get; set; }
    public long min { get; set; }
    public long max { get; set; }
    public bool list { get; set; }
    public Z2MState[] states { get; set; }
    public object value { get; set; }
    public long lastUpdate { get; set; }
}