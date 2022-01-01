using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZwaveMqttTemplater.Z2M;

class Z2MApiClient
{
    private readonly IManagedMqttClient _mqtt;
    private readonly string _topicPrefix;
    private int _nextMessageId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<MqttApplicationMessage>> _tasks = new();

    public Z2MApiClient(IManagedMqttClient mqtt, string prefix = "zwavejs2mqtt/")
    {
        _mqtt = mqtt;
        _topicPrefix = prefix + "_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/";
    }

    public async Task Start()
    {
        string subscription = _topicPrefix + "+";
        await _mqtt.SubscribeAsync(subscription);

        _mqtt.AddApplicationMessageReceivedHandler(OnCallback);
    }

    public async Task<Task<MqttApplicationMessage>> SendCommand(string api, object[] args = null, CancellationToken token = default)
    {
        string topic = _topicPrefix + api + "/set";
        int messageId = Interlocked.Increment(ref _nextMessageId);

        string requestJson = JsonConvert.SerializeObject(new { messageId, args });

        TaskCompletionSource<MqttApplicationMessage> taskCompletion = new TaskCompletionSource<MqttApplicationMessage>();

        _tasks.TryAdd(messageId, taskCompletion);
        await _mqtt.PublishAsync(topic, requestJson);

        return taskCompletion.Task;
    }

    private Task OnCallback(MqttApplicationMessageReceivedEventArgs arg)
    {
        string topic = arg.ApplicationMessage.Topic;
        if (!topic.StartsWith(_topicPrefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        JObject obj = JObject.Parse(arg.ApplicationMessage.ConvertPayloadToString());
        JToken origin = obj["origin"];
        int messageId = origin.Value<int>("messageId");

        if (!_tasks.TryRemove(messageId, out TaskCompletionSource<MqttApplicationMessage> tcs))
            return Task.CompletedTask;

        MqttApplicationMessage message = arg.ApplicationMessage;
        tcs.SetResult(message);

        return Task.CompletedTask;
    }

}

static class Z2MApiClientExtensions
{
    private static async Task<TResult> InvokeJson<TResult>(this Z2MApiClient client, string api, object[] args = null)
    {
        Task<MqttApplicationMessage> tsk = await client.SendCommand("getNodes");
        MqttApplicationMessage res = await tsk;

        string json = res.ConvertPayloadToString();

        Z2MApiCallResult<TResult> result = JsonConvert.DeserializeObject<Z2MApiCallResult<TResult>>(json);

        if (!result.Success)
            throw new Exception("");

        return result.Result;
    }

    public static async Task<List<Z2MNode>> GetNodes(this Z2MApiClient client)
    {
        return await client.InvokeJson<List<Z2MNode>>("getNodes");
    }

    public static async Task PingNode(this Z2MApiClient client, int nodeId)
    {
        await await client.SendCommand("pingNode", new object[] { nodeId });
    }

    public static async Task HealNode(this Z2MApiClient client, int nodeId)
    {
        await await client.SendCommand("healNode", new object[] { nodeId });
    }

    public static async Task RefreshCCValues(this Z2MApiClient client, int nodeId, int commandClass)
    {
        await await client.SendCommand("refreshCCValues", new object[] { nodeId , commandClass });
    }
}