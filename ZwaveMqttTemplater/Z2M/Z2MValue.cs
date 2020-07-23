namespace ZwaveMqttTemplater.Z2M
{
    public class Z2MValue
    {
        public string value_id { get; set; }
        public int node_id { get; set; }
        public int class_id { get; set; }
        public string type { get; set; }
        public string genre { get; set; }
        public int instance { get; set; }
        public int index { get; set; }
        public string label { get; set; }
        public string units { get; set; }
        public string help { get; set; }
        public bool read_only { get; set; }
        public bool write_only { get; set; }
        public int min { get; set; }
        public int max { get; set; }
        public bool is_polled { get; set; }
        public object value { get; set; }
        public string[] values { get; set; }
        public long lastUpdate { get; set; }
    }
}