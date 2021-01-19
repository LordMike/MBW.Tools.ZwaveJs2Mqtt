using System;
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
using MQTTnet.Client.Unsubscribing;
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

            MqttStore store = new MqttStore(mqttClient);

            await store.Load("homeassistant/#", "zwave2mqtt/#", "zwavejs2mqtt/#");

            try
            {
                Z2MContainer nodes = await GetNodes(mqttClient);

                //DumpConfigs(nodes);
                //DumpFirmwares(nodes);

                //await HandleAssociationsConfig(mqttClient, store, nodes);
                await HandleHassConfigs(store);
                //await HandleDeviceConfigs(store, nodes);

                List<string> topics = store.GetTopicsToSet().ToList();

                if (topics.Any())
                {
                    Console.WriteLine("Sending to:");

                    foreach (string topic in topics)
                        Console.WriteLine("> " + topic);

                    Console.WriteLine("Ok?");
                    Console.ReadLine();

                    await store.FlushTopicsToSet();
                }
                else
                {
                    Console.WriteLine("No topics to set");
                }
            }
            finally
            {
                await mqttClient.DisconnectAsync(new MqttClientDisconnectOptions { ReasonCode = MqttClientDisconnectReason.NormalDisconnection, ReasonString = "Shutting down" });
            }
        }

        private static void DumpConfigs(Z2MContainer nodes)
        {
            IEnumerable<Z2MNode> toList = nodes.GetAll();

            foreach (Z2MNode z2MNode in toList)
            {
                Console.WriteLine($"{z2MNode.id}: {z2MNode.productLabel} {z2MNode.productDescription} ({z2MNode.manufacturer})");
                Console.WriteLine($"  Name: {z2MNode.name}   fw: {z2MNode.firmwareVersion}");

                foreach ((string key, Z2MValue value) in z2MNode.values)
                {
                    if (value.commandClass != 112)
                        continue;

                    Console.WriteLine($"  {key}: {value.value}  ({value.label})");
                }

                Console.WriteLine();
            }
        }

        private static void DumpFirmwares(Z2MContainer nodes)
        {
            IOrderedEnumerable<Z2MNode> sorted = nodes.GetAll()
                .OrderBy(s => s.manufacturerId)
                .ThenBy(s => s.productId);

            foreach (Z2MNode z2MNode in sorted)
            {
                Console.WriteLine($"{z2MNode.id}: {z2MNode.productLabel} {z2MNode.productDescription} ({z2MNode.manufacturer})  fw: {z2MNode.firmwareVersion}");
            }
        }

        private static async Task<Z2MContainer> GetNodes(IMqttClient mqttClient)
        {
            Z2MApiCallResult<List<Z2MNode>> res = await Z2MHelpers.CallZwavejsApi<List<Z2MNode>>(mqttClient, "getNodes");

            return new Z2MContainer(res.Result);
        }

        private static async Task<Z2mAssociation[]> GetAssociations(IMqttClient mqttClient, int node, int group)
        {
            Z2MApiCallResult<Z2mAssociation[]> res = await Z2MHelpers.CallZwavejsApi<Z2mAssociation[]>(mqttClient, "getAssociations", new object[] { node, group });

            return res.Result;
        }

        private static async Task HandleAssociationsConfig(IMqttClient client, MqttStore store, Z2MContainer nodes)
        {
            DesiredAssociationsContainer desired = JsonConvert.DeserializeObject<DesiredAssociationsContainer>(File.ReadAllText("AssociationsConfig.json"));

            foreach ((string key, DesiredAssociations value) in desired.Nodes)
            {
                Z2MNode node = nodes.GetByName(key).Single();

                foreach (Z2MGroup z2MGroup in node.groups)
                {
                    int groupId = z2MGroup.value;
                    List<Z2mAssociation> desiredLinks = value.GetLinks(nodes, groupId).ToList();

                    Z2mAssociation[] existing = await GetAssociations(client, node.id, groupId);

                    List<Z2mAssociation> toRemove = existing.Where(s => desiredLinks.All(x => s != x)).ToList();
                    List<Z2mAssociation> toAdd = desiredLinks.Where(s => existing.All(x => s != x)).ToList();

                    foreach (Z2mAssociation item in toRemove)
                    {
                        Z2MNode otherNode = nodes.GetNode(item.NodeId);
                        Console.WriteLine($"Node {node.id} {node.name}, remove group {groupId} ({z2MGroup.text}) => {item.NodeId}.{item.Endpoint} ({otherNode.name})");
                    }

                    if (toRemove.Any())
                        store.SetBlindly("zwavejs2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/removeAssociations/set", JsonConvert.SerializeObject(new
                        {
                            args = new object[]
                            {
                            node.id, groupId, toRemove
                            }
                        }));

                    foreach (Z2mAssociation item in toAdd)
                    {
                        Z2MNode otherNode = nodes.GetNode(item.NodeId);
                        Console.WriteLine($"Node {node.id} {node.name}, add group {groupId} ({z2MGroup.text}) => {item.NodeId}.{item.Endpoint} ({otherNode.name})");
                    }

                    if (toAdd.Any())
                        store.SetBlindly("zwavejs2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/addAssociations/set", JsonConvert.SerializeObject(new
                        {
                            args = new object[]
                            {
                            node.id, groupId, toAdd
                            }
                        }));

                    if (toAdd.Any() || toRemove.Any())
                        return;
                }
            }
        }

        private static async Task HandleHassConfigs(MqttStore store)
        {
            void HandleHassConfigs(string product, string nodeName)
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

                        store.Set(discoveryTopic, JsonConvert.SerializeObject(discoveryDoc), true);
                    }
                }
            }

            HandleHassConfigs("AeotecSmartSwitch7", "powerplug_1");
            HandleHassConfigs("AeotecSmartSwitch7", "powerplug_2");
            HandleHassConfigs("AeotecSmartDimmer6", "powerplug_3");

            //HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "door_1_1");
            //HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_20_2");
            //HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_21_2");

            HandleHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "door_1_1");
            HandleHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "window_20_2");
            HandleHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "window_21_2");
            HandleHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "door_30_1");
            HandleHassConfigs("AeotecDoorWindowSensor6_basicsetkind", "door_4_2");

            HandleHassConfigs("AeotecDoorWindowSensor7", "window_1_2");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_1_3");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_1_4");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_2_2");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_3_2");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_4_3");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_5_2");
            HandleHassConfigs("AeotecDoorWindowSensor7", "window_22_1");

            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_2");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_1");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_30_1");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_30_2");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_20");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_21");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_22");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_3");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_4");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_5");
            HandleHassConfigs("LogicsoftZDB5100", "wallswitch_31");
        }

        private static async Task HandleDeviceConfigs(MqttStore store, Z2MContainer z2MContainer)
        {
            string[] configLines = await File.ReadAllLinesAsync("DeviceConfigsFile.txt");

            List<Z2MNode> nodes = new List<Z2MNode>();

            // Apply all configs in order to get final setup
            // product:ZDB5100 Matrix	1-31-2[0x33]	0
            Regex lineParser = new Regex(@"^(?<type>\w+):(?<filter>[\w\s]+)\t(?<key>[0-9\-]+)\t(?<value>[^#\n]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Dictionary<ValueKey, object> desiredValues = new Dictionary<ValueKey, object>();

            foreach (string configLine in configLines)
            {
                Match match = lineParser.Match(configLine);
                if (!match.Success)
                    continue;

                nodes.Clear();

                switch (match.Groups["type"].Value)
                {
                    case "name":
                        nodes.AddRange(z2MContainer.GetByNameFilter(match.Groups["filter"].Value));
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
                string valueKey = match.Groups["key"].Value;

                foreach (Z2MNode z2MNode in nodes)
                {
                    if (!z2MNode.values.TryGetValue(valueKey, out Z2MValue value))
                        continue;

                    object newVal = ConvertZ2MValue(value, match.Groups["value"].Value);
                    desiredValues[new ValueKey(z2MNode.id, valueKey)] = newVal;
                }
            }

            foreach ((ValueKey key, object value) in desiredValues.OrderBy(s => s.Key.NodeId).ThenBy(s => s.Key.Key))
            {
                // Get existing value
                Z2MNode node = z2MContainer.GetNode(key.NodeId);
                Z2MValue z2MValue = z2MContainer.GetValue(key.NodeId, key.Key);
                object currentVal = z2MValue.value;
                if (value.Equals(currentVal))
                {
                    // Skip this
                    continue;
                }

                // > zwavejs2mqtt/wallswitch_4/112/0/2
                // DATA

                string topic = $"zwavejs2mqtt/{node.name}/{key.Key.Replace('-', '/')}/set";
                store.Set(topic, JsonConvert.SerializeObject(value));
            }
        }

        private static object ConvertZ2MValue(Z2MValue spec, string val = null)
        {
            Z2MState state;
            if (spec.type == "number" &&
                spec.list &&
                (state = spec.states.FirstOrDefault(s => s.text == val)) != null)
            {
                return Convert.ChangeType(state.value, typeof(long));
            }

            if (spec.type == "number")
            {
                return Convert.ToInt64(val);
            }

            throw new Exception();
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

    class Z2mAssociation : IEquatable<Z2mAssociation>
    {
        [JsonProperty("nodeId")]
        public int NodeId { get; set; }

        [JsonProperty("endpoint")]
        public int Endpoint { get; set; }

        public Z2mAssociation(int nodeId, int endpoint)
        {
            NodeId = nodeId;
            Endpoint = endpoint;
        }

        public bool Equals(Z2mAssociation other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return NodeId == other.NodeId && Endpoint == other.Endpoint;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((Z2mAssociation)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NodeId, Endpoint);
        }

        public static bool operator ==(Z2mAssociation left, Z2mAssociation right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Z2mAssociation left, Z2mAssociation right)
        {
            return !Equals(left, right);
        }
    }

    class DesiredAssociationsContainer
    {
        public Dictionary<string, DesiredAssociations> Nodes { get; set; }
    }

    class DesiredAssociations
    {
        /// <summary>
        /// Group => Node.Endpoint
        /// </summary>
        public Dictionary<int, string[]> Links { get; set; }

        public IEnumerable<Z2mAssociation> GetLinks(Z2MContainer nodes, int groupId)
        {
            if (Links == null || !Links.TryGetValue(groupId, out string[] links))
                yield break;

            foreach (string link in links)
            {
                string[] split = link.Split('.');
                string node = split[0];
                int endpoint = split.Length == 2 ? Convert.ToInt32(split[1]) : 0;

                if (int.TryParse(node, out int nodeId))
                {
                    yield return new Z2mAssociation(nodeId, Convert.ToInt32(split[1]));
                }
                else
                {
                    Z2MNode target = nodes.GetByName(node).FirstOrDefault();
                    if (target == null)
                        throw new Exception();

                    yield return new Z2mAssociation(target.id, endpoint);
                }
            }
        }
    }
}
