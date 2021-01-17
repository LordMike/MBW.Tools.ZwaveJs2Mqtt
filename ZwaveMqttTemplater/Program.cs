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
                //Z2MContainer nodes = await GetNodes(mqttClient);

                //DumpConfigs(nodes);
                //DumpFirmwares(nodes);

                await HandleHassConfigs(store);
                //await HandleDeviceConfigs(mqttClient, nodes);

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
                Z2MValue fwConfig = z2MNode.values.Values.FirstOrDefault(s =>
                    s.class_id == 134 && s.instance == 1 && s.index == 2);

                Console.WriteLine($"{z2MNode.node_id}: {z2MNode.product} ({z2MNode.manufacturer})");
                Console.WriteLine($"  Name: {z2MNode.name}   fw: {fwConfig?.value}");

                foreach ((string key, Z2MValue value) in z2MNode.values)
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
            IOrderedEnumerable<Z2MNode> toList = nodes.GetAll()
                .OrderBy(s => s.manufacturerid)
                .ThenBy(s => s.productid);

            foreach (Z2MNode z2MNode in toList)
            {
                Z2MValue fwConfig = z2MNode.values.Values.FirstOrDefault(s =>
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

                foreach (Z2MNode z2MNode in nodes.Result)
                {
                    foreach ((string key, Z2MValue value) in z2MNode.values)
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
                throw new Exception();

            // clear this output
            await mqttClient.PublishAsync("zwave2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/getNodes", new byte[0]);

            return container;
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

            HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "door_1_1");
            HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_20_2");
            HandleHassConfigs("AeotecDoorWindowSensor6_alarmkind", "window_21_2");
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

                foreach (Z2MNode z2MNode in nodes.Where(s => s.node_id == 9))
                {
                    Z2MValue valueSpec = z2MContainer.GetCurrentValue(z2MNode.node_id, @class, instance, index);
                    object newVal = ConvertZ2MValue(valueSpec, match.Groups["value"].Value);

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
            foreach ((ValueKey key, object value) in values.OrderBy(s => s.Key.NodeId).ThenBy(s => s.Key.Instance).ThenBy(s => s.Key.Index))
            {
                // Get existing value
                Z2MNode node = z2MContainer.GetNode(key.NodeId);
                Z2MValue z2MValue = z2MContainer.GetCurrentValue(key.NodeId, key.Class, key.Instance, key.Index);
                object currentVal = ConvertZ2MValue(z2MValue);
                if (value.Equals(currentVal))
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

                store.SetBlindly(topic, JsonConvert.SerializeObject(setValueArgs));
            }
        }

        private static object ConvertZ2MValue(Z2MValue spec, object val = null)
        {
            val ??= spec.value;

            switch (spec.type)
            {
                case "short":
                    return Convert.ToInt16(val);
                case "int":
                    return Convert.ToInt32(val);
                case "byte":
                    return Convert.ToByte(val);
                case "list":
                    {
                        if (val is string asString && int.TryParse(asString, out int asInt))
                            return spec.values[asInt];
                        return val;
                    }
                case "bool":
                    {
                        if (val is bool asBool)
                            return asBool;
                        if (val is string asString)
                        {
                            if (int.TryParse(asString, out int asInt))
                                return asInt == 1;
                            if (bool.TryParse(asString, out asBool))
                                return asBool;
                        }
                        throw new Exception();
                    }
                default:
                    throw new Exception();
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
}
