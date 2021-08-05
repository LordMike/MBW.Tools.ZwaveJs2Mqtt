using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater
{
    static class Z2MHelpers
    {
        public static async Task<Z2MApiCallResult<T>> CallZwavejsApi<T>(IMqttClient client, string method, object[] args = null)
        {
            string prefix = $"zwavejs2mqtt/_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/{method}";
            string setCmd = $"{prefix}/set";
            var argsJarray = args == null ? null : JArray.FromObject(args);

            ManualResetEvent stopEvent = new ManualResetEvent(false);
            await using Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(30000, Timeout.Infinite);

            Z2MApiCallResult<T> result = null;

            client.UseApplicationMessageReceivedHandler(eventArgs =>
            {
                if (eventArgs.ApplicationMessage.Payload == null || result != null)
                    return;

                string str = eventArgs.ApplicationMessage.ConvertPayloadToString();
                str = JsonConvert.DeserializeObject<JToken>(str).ToString(Formatting.Indented);

                var tmpResult = JsonConvert.DeserializeObject<Z2MApiCallResult<T>>(str);

                // Ensure args match
                if (argsJarray != null)
                {
                    if (tmpResult.Args.SequenceEqual(argsJarray))
                    {
                        // Identical args
                        result = tmpResult;
                        stopEvent.Set();
                    }
                }
                else
                {
                    // No args to check
                    result = tmpResult;
                    stopEvent.Set();
                }
            });

            await client.PublishAsync(prefix, Array.Empty<byte>()); // clear old output

            await client.SubscribeAsync(
                new TopicFilter { Topic = prefix }
            );

            var argsBytes = argsJarray == null
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { args = argsJarray }));
            await client.PublishAsync(setCmd, argsBytes); // request new doc

            stopEvent.WaitOne();

            if (result == null)
                throw new Exception();

            //await client.PublishAsync(prefix, Array.Empty<byte>());

            return result;
        }
    }
}