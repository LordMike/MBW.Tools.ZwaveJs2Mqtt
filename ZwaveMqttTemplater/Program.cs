using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Client.Disconnecting;
using MQTTnet.Client.Options;
using MQTTnet.Formatter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConsoleApp35
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

            Dictionary<string, byte[]> knownConfigs = new Dictionary<string, byte[]>();

            ManualResetEvent stopEvent = new ManualResetEvent(false);
            Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(2500, Timeout.Infinite);

            await mqttClient.ConnectAsync(mqttOptions, default);

            try
            {
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
                messages.AddRange(Prepare("AeotecSmartSwitch7", "powerplug_1"));
                messages.AddRange(Prepare("AeotecSmartSwitch7", "powerplug_2"));
                messages.AddRange(Prepare("AeotecSmartDimmer6", "powerplug_3"));
                messages.AddRange(Prepare("LogcsoftZDB5100", "wallswitch_2"));
                messages.AddRange(Prepare("AeotecDoorWindowSensor7", "door_4_2"));

                messages.RemoveAll(x => knownConfigs.TryGetValue(x.Topic, out byte[] existingDoc) && existingDoc.SequenceEqual(x.Payload));

                if (messages.Any())
                {
                    await mqttClient.PublishAsync(messages);

                    foreach (MqttApplicationMessage message in messages)
                    {
                        Console.WriteLine($"Sent {message.Topic}");
                    }
                }
            }
            finally
            {
                await mqttClient.DisconnectAsync(new MqttClientDisconnectOptions { ReasonCode = MqttClientDisconnectReason.NormalDisconnection, ReasonString = "Shutting down" });
            }
        }

        private static IEnumerable<MqttApplicationMessage> Prepare(string product, string nodeName)
        {
            JObject doc = ReadDoc(product);

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
                JObject discoveryDocs = typeProp.Value as JObject;

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
                    JToken? item = asObj[key];
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

        private static JObject ReadDoc(string name)
        {
            Type type = typeof(Program);
            Assembly assembly = type.Assembly;

            using Stream? strm = assembly.GetManifestResourceStream($"{type.Namespace}.docs.{name}.json");
            using StreamReader sr = new StreamReader(strm, Encoding.UTF8);

            return JObject.Parse(sr.ReadToEnd());
        }
    }
}
