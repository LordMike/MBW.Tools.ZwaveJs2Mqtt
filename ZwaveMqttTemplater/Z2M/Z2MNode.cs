using System.Collections.Generic;

namespace ZwaveMqttTemplater.Z2M
{
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
}