using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZwaveMqttTemplater
{
    static class Z2MHelpers
    {
        public static async Task<T> CallZwavejsApi<T>(IMqttClient client, string method, object[] args = null)
        {
            JToken token = await CallZwavejsApi(client, method, args);

            return token.ToObject<T>();
        }

        public static async Task<JToken> CallZwavejsApi(IMqttClient client, string method, object[] args = null)
        {
            string prefix = $"zwavejs2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/{method}";
            string setCmd = $"{prefix}/set";
            var argsBytes = args == null
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { data = args }));

            ManualResetEvent stopEvent = new ManualResetEvent(false);
            await using Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(30000, Timeout.Infinite);

            JToken result = null;

            client.UseApplicationMessageReceivedHandler(eventArgs =>
            {
                if (eventArgs.ApplicationMessage.Payload == null || result != null)
                    return;

                string str = eventArgs.ApplicationMessage.ConvertPayloadToString();
                str = JsonConvert.DeserializeObject<JToken>(str).ToString(Formatting.Indented);

                result = JsonConvert.DeserializeObject<JToken>(str);

                stopEvent.Set();
            });

            await client.PublishAsync(prefix, Array.Empty<byte>()); // clear old output

            await client.SubscribeAsync(
                new TopicFilter { Topic = prefix }
            );

            await client.PublishAsync(setCmd, argsBytes); // request new doc

            stopEvent.WaitOne();

            if (result == null)
                throw new Exception();

            await client.PublishAsync(prefix, Array.Empty<byte>());

            return result;
        }
    }
}