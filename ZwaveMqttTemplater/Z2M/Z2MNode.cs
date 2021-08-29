using System.Collections.Generic;

namespace ZwaveMqttTemplater.Z2M
{
    class Z2MNode
    {
        public int id { get; set; }
        public string deviceId { get; set; }
        public string manufacturer { get; set; }
        public int? manufacturerId { get; set; }
        public int? productType { get; set; }
        public int? productId { get; set; }
        public string name { get; set; }
        public string loc { get; set; }
        public Dictionary<string, Z2MValue> values { get; set; }
        public Z2MGroup[] groups { get; set; }
        public int[] neighbors { get; set; }
        public bool ready { get; set; }
        public bool available { get; set; }
        public bool failed { get; set; }
        public long? lastActive { get; set; }
        public bool interviewCompleted { get; set; }
        public string firmwareVersion { get; set; }
        public bool isBeaming { get; set; }
        //public bool isSecure { get; set; }
        public bool keepAwake { get; set; }
        public object maxBaudRate { get; set; }
        public bool? isRouting { get; set; }
        public bool isFrequentListening { get; set; }
        public bool isListening { get; set; }
        public string status { get; set; }
        public string interviewStage { get; set; }
        public string productLabel { get; set; }
        public string productDescription { get; set; }
        public int zwaveVersion { get; set; }
        public Deviceclass deviceClass { get; set; }
    }
}