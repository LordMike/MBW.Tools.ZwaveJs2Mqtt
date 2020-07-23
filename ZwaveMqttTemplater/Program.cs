﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater
{
    class Program
    {
        private const string HassPrefix = "homeassistant";

        static async Task Main(string[] args)
        {
            IMqttClient mqttClient = new MqttFactory()
                .CreateMqttClient();

            IMqttClientOptions mqttOptions = new MqttClientOptionsBuilder()
                .WithTcpServer("queue.home")
                .WithCredentials("mqtt_speccer", (string)null)
                .WithCleanSession()
                .WithProtocolVersion(MqttProtocolVersion.V500)
                .Build();

            await mqttClient.ConnectAsync(mqttOptions, default);

            try
            {
                Z2MContainer nodes = await GetNodes(mqttClient);

                //DumpConfigs(nodes);
                //DumpFirmwares(nodes);

                //await HandleHassConfigs(mqttClient);
                await HandleDeviceConfigs(mqttClient, nodes);
            }
            finally
            {
                await mqttClient.DisconnectAsync(new MqttClientDisconnectOptions { ReasonCode = MqttClientDisconnectReason.NormalDisconnection, ReasonString = "Shutting down" });
            }
        }

        private static void DumpConfigs(Z2MContainer nodes)
        {
            var toList = nodes.GetAll();

            foreach (var z2MNode in toList)
            {
                var fwConfig = z2MNode.values.Values.FirstOrDefault(s =>
                    s.class_id == 134 && s.instance == 1 && s.index == 2);

                Console.WriteLine($"{z2MNode.node_id}: {z2MNode.product} ({z2MNode.manufacturer})");
                Console.WriteLine($"  Name: {z2MNode.name}   fw: {fwConfig?.value}");

                foreach (var (key, value) in z2MNode.values)
                {
                    if (value.genre != "config")
                        continue;

                    Console.WriteLine($"  {value.class_id}-{value.instance}-{value.index}: {value.value}");
                }

                Console.WriteLine();
            }
        }

        private static void DumpFirmwares(Z2MContainer nodes)
        {
            var toList = nodes.GetAll()
                .OrderBy(s=>s.manufacturerid)
                .ThenBy(s=>s.productid);

            foreach (var z2MNode in toList)
            {
                var fwConfig = z2MNode.values.Values.FirstOrDefault(s =>
                    s.class_id == 134 && s.instance == 1 && s.index == 2);

                Console.WriteLine($"{z2MNode.node_id}: {z2MNode.product} ({z2MNode.manufacturer})  fw: {fwConfig?.value}");
            }
        }

        private static async Task<Z2MContainer> GetNodes(IMqttClient mqttClient)
        {
            ManualResetEvent stopEvent = new ManualResetEvent(false);
            await using Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(10000, Timeout.Infinite);

            Z2MContainer container = null;

            mqttClient.UseApplicationMessageReceivedHandler(eventArgs =>
            {
                if (eventArgs.ApplicationMessage.Payload == null)
                    return;

                string str = eventArgs.ApplicationMessage.ConvertPayloadToString();

                Z2MApiCallResult<List<Z2MNode>> nodes =
                    JsonConvert.DeserializeObject<Z2MApiCallResult<List<Z2MNode>>>(str);

                container = new Z2MContainer(nodes.Result);

                foreach (var z2MNode in nodes.Result)
                {
                    foreach (var (key, value) in z2MNode.values)
                    {
                        switch (value.type)
                        {
                            case "byte":
                                value.value = Convert.ChangeType(value.value, typeof(byte));
                                break;
                            case "int":
                                value.value = Convert.ChangeType(value.value, typeof(int));
                                break;
                        }
                    }
                }

                stopEvent.Set();
            });


            await mqttClient.PublishAsync("zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/getNodes", new byte[0]); // clear old output
            await mqttClient.PublishAsync("zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/getNodes/set", new byte[0]); // request new doc

            await mqttClient.SubscribeAsync(
                new TopicFilter { Topic = "zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/getNodes" }
            );

            stopEvent.WaitOne();
            timer.Change(Timeout.Infinite, Timeout.Infinite);

            if (container == null)
                throw new Exception("");
            
            // clear this output
            await mqttClient.PublishAsync("zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/getNodes", new byte[0]); 

            return container;
        }

        private static async Task HandleHassConfigs(IMqttClient mqttClient)
        {
            ManualResetEvent stopEvent = new ManualResetEvent(false);
            Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(2500, Timeout.Infinite);

            Dictionary<string, byte[]> knownConfigs = new Dictionary<string, byte[]>();

            mqttClient.UseApplicationMessageReceivedHandler(eventArgs =>
            {
                knownConfigs[eventArgs.ApplicationMessage.Topic] = eventArgs.ApplicationMessage.Payload;
                timer.Change(500, Timeout.Infinite);
            });

            await mqttClient.SubscribeAsync(
                new TopicFilter { Topic = $"{HassPrefix}/+/+/config" },
                new TopicFilter { Topic = $"{HassPrefix}/+/+/+/config" }
            );

            stopEvent.WaitOne();

            List<MqttApplicationMessage> messages = new List<MqttApplicationMessage>();
            messages.AddRange(PrepareHassConfigs("AeotecSmartSwitch7", "pool_uv"));
            messages.AddRange(PrepareHassConfigs("AeotecSmartDimmer6", "pool_pump"));

            messages.AddRange(PrepareHassConfigs("AeotecSmartSwitch7", "powerplug_1"));

            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor6_alarmkind", "door_1_1"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_20_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_21_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "door_30_1"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "door_4_2"));

            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_1_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_1_3"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_1_4"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_2_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_3_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_4_3"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_5_2"));
            messages.AddRange(PrepareHassConfigs("AeotecDoorWindowSensor7", "window_22_1"));

            //messages.AddRange(Prepare("LogicsoftZHC5010", "wallswitch_3"));

            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_2"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_1"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_30_1"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_30_2"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_20"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_21"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_22"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_3"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_4"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_5"));
            messages.AddRange(PrepareHassConfigs("LogicsoftZDB5100", "wallswitch_31"));

            messages.RemoveAll(x =>
                knownConfigs.TryGetValue(x.Topic, out byte[] existingDoc) && existingDoc.SequenceEqual(x.Payload));

            if (messages.Any())
            {
                await mqttClient.PublishAsync(messages);

                foreach (MqttApplicationMessage message in messages)
                {
                    Console.WriteLine($"Sent {message.Topic}");
                }
            }
        }

        private static async Task HandleDeviceConfigs(IMqttClient mqttClient, Z2MContainer z2MContainer)
        {
            string[] configLines = await File.ReadAllLinesAsync("DeviceConfigsFile.txt");

            List<Z2MNode> nodes = new List<Z2MNode>();

            // Apply all configs in order to get final setup
            // product:ZDB5100 Matrix	1-31	0
            Regex lineParser = new Regex(@"^(?<type>\w+):(?<filter>[\w\s]+)\t(?<class>[0-9]+)-(?<instance>[0-9]+)-(?<index>[0-9]+)\t(?<value>[^#\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Dictionary<ValueKey, object> values = new Dictionary<ValueKey, object>();

            foreach (string configLine in configLines)
            {
                Match match = lineParser.Match(configLine);
                if (!match.Success)
                    continue;

                nodes.Clear();

                switch (match.Groups["type"].Value)
                {
                    case "name":
                        nodes.AddRange(z2MContainer.GetByName(match.Groups["filter"].Value));
                        break;
                    case "product":
                        nodes.AddRange(z2MContainer.GetByProduct(match.Groups["filter"].Value));
                        break;
                    case "manufacturer":
                        nodes.AddRange(z2MContainer.GetByManufacturer(match.Groups["filter"].Value));
                        break;
                    case "id":
                        nodes.AddRange(z2MContainer.GetById(match.Groups["filter"].Value));
                        break;
                    default:
                        throw new Exception();
                }

                // Set values
                int @class = Convert.ToInt32(match.Groups["class"].Value);
                int instance = Convert.ToInt32(match.Groups["instance"].Value);
                int index = Convert.ToInt32(match.Groups["index"].Value);

                foreach (Z2MNode z2MNode in nodes)
                {
                    var valueSpec = z2MContainer.GetCurrentValue(z2MNode.node_id, @class, instance, index);

                    object newVal;
                    switch (valueSpec.type)
                    {
                        case "int":
                            newVal = Convert.ToInt32(match.Groups["value"].Value);
                            break;
                        case "byte":
                            newVal = Convert.ToByte(match.Groups["value"].Value);
                            break;
                        case "list":
                            newVal = valueSpec.values[Convert.ToInt32(match.Groups["value"].Value)];
                            break;
                        default:
                            throw new Exception();
                    }

                    values[new ValueKey
                    {
                        NodeId = z2MNode.node_id,
                        Class = @class,
                        Instance = instance,
                        Index = index
                    }] = newVal;
                }
            }

            // Prepare messages, send MQTT
            List<MqttApplicationMessage> messages = new List<MqttApplicationMessage>();

            //zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/setValue/set
            //{
            //    "args": [node, class, instance, index, value]
            //}

            string topic = "zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/setValue/set";
            foreach (var (key, value) in values.OrderBy(s => s.Key.NodeId).ThenBy(s => s.Key.Instance).ThenBy(s => s.Key.Index))
            {
                // Get existing value
                var node = z2MContainer.GetNode(key.NodeId);
                var currentVal = z2MContainer.GetCurrentValue(key.NodeId, key.Class, key.Instance, key.Index);
                if (value.Equals(currentVal.value))
                {
                    // Skip this
                    continue;
                }

                var setValueArgs = new
                {
                    args = new[]
                    {
                        key.NodeId,
                        key.Class,
                        key.Instance,
                        key.Index,
                        value
                    }
                };

                byte[] setValueBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(setValueArgs));

                messages.Add(new MqttApplicationMessage
                {
                    Topic = topic,
                    Payload = setValueBytes
                });

                Console.WriteLine($"Sending ({node.product} \"{node.name}\" / {key}) {currentVal.label}: {value}");
            }

            if (messages.Any())
            {
                Console.WriteLine("Press any key to send messages");
                Console.ReadLine();

                await mqttClient.PublishAsync(messages);
            }
        }

        private static IEnumerable<MqttApplicationMessage> PrepareHassConfigs(string product, string nodeName)
        {
            JObject doc = ReadDoc("docs", product);

            // Replace
            doc = (JObject)Transform(doc, token =>
            {
                if (token.Type == JTokenType.String)
                    return JToken.FromObject(token.Value<string>().Replace("NODE_NAME", nodeName));

                return token;
            });

            foreach (JProperty typeProp in doc.Properties())
            {
                string type = typeProp.Name;
                JObject discoveryDocs = (JObject)typeProp.Value;

                foreach (KeyValuePair<string, JToken> discoveryDocItem in discoveryDocs)
                {
                    string entityName = discoveryDocItem.Key;
                    JToken discoveryDoc = discoveryDocItem.Value;
                    string discoveryTopic = $"{HassPrefix}/{type}/{nodeName}/{entityName}/config";

                    byte[] discoveryBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(discoveryDoc));

                    yield return new MqttApplicationMessage
                    {
                        Topic = discoveryTopic,
                        Retain = true,
                        Payload = discoveryBytes
                    };
                }
            }
        }

        private static JToken Transform(JToken token, Func<JToken, JToken> action)
        {
            token = action(token);

            if (token is JObject asObj)
            {
                foreach (string key in asObj.Properties().Select(s => s.Name).ToArray())
                {
                    JToken item = asObj[key];
                    item = Transform(item, action);
                    asObj[key] = item;
                }
            }
            else if (token is JArray asArray)
            {
                for (int i = 0; i < asArray.Count; i++)
                {
                    JToken item = asArray[i];
                    item = Transform(item, action);
                    asArray[i] = item;
                }
            }

            return token;
        }

        private static JObject ReadDoc(string kind, string name)
        {
            Type type = typeof(Program);
            Assembly assembly = type.Assembly;

            string[] available = assembly.GetManifestResourceNames();

            using Stream strm = assembly.GetManifestResourceStream($"{type.Namespace}.{kind}.{name}.json");
            using StreamReader sr = new StreamReader(strm, Encoding.UTF8);

            return JObject.Parse(sr.ReadToEnd());
        }
    }

    class Z2MContainer
    {
        private readonly List<Z2MNode> _nodes;

        public Z2MContainer(List<Z2MNode> nodes)
        {
            _nodes = nodes;
        }

        private IEnumerable<Z2MNode> GetBaseQuery()
        {
            return _nodes.Where(s => !s.failed);
        }

        public IEnumerable<Z2MNode> GetByName(string filter)
        {
            // TODO: wildcards
            return GetBaseQuery().Where(s => s.name?.Contains(filter) ?? false);
        }

        public IEnumerable<Z2MNode> GetByProduct(string filter)
        {
            // TODO: wildcards
            return GetBaseQuery().Where(s => s.product.Contains(filter));
        }

        public IEnumerable<Z2MNode> GetById(string filter)
        {
            throw new NotImplementedException();
        }

        public IEnumerable<Z2MNode> GetByManufacturer(string filter)
        {
            throw new NotImplementedException();
        }

        public Z2MValue GetCurrentValue(int nodeId, int commandClass, int instance, int index)
        {
            var node = _nodes[nodeId];

            if (!node.values.TryGetValue($"{commandClass}-{instance}-{index}", out Z2MValue valueSpec))
                throw new Exception();

            return valueSpec;
        }

        public IEnumerable<Z2MNode> GetAll(bool includeRemoved = false)
        {
            return _nodes.Where(s => includeRemoved || !s.failed);
        }

        public Z2MNode GetNode(int nodeId)
        {
            return _nodes[nodeId];
        }
    }

    class ValueKey : IEquatable<ValueKey>
    {
        public int NodeId { get; set; }

        public int Class { get; set; }

        public int Instance { get; set; }

        public int Index { get; set; }

        public override string ToString()
        {
            return $"{NodeId}-{Class}-{Instance}-{Index}";
        }

        public bool Equals(ValueKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return NodeId == other.NodeId && Class == other.Class && Instance == other.Instance && Index == other.Index;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ValueKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NodeId, Instance, Index);
        }
    }
}
