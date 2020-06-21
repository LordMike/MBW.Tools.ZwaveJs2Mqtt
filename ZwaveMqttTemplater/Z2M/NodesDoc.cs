using System.Collections.Generic;

namespace ZwaveMqttTemplater.Z2M
{
    class Z2MApiCallResult<T>
    {
        public bool Success { get; set; }

        public string Message { get; set; }

        public T Result { get; set; }
    }

    class Z2MNode
    {
        public int node_id { get; set; }
        public string device_id { get; set; }
        public string manufacturer { get; set; }
        public string manufacturerid { get; set; }
        public string name { get; set; }
        public string product { get; set; }
        public string producttype { get; set; }
        public string productid { get; set; }
        public string type { get; set; }
        public Dictionary<string, Z2MValue> values { get; set; }
        public object[] groups { get; set; }
        public int[] neighborns { get; set; }
        public bool ready { get; set; }
        public bool available { get; set; }
        public bool failed { get; set; }
        public long? lastActive { get; set; }
        public string status { get; set; }
    }

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
