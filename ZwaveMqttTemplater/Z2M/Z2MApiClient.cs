using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZwaveMqttTemplater.Mqtt;

namespace ZwaveMqttTemplater.Z2M;

internal class Z2MApiClient
{
    private readonly ILogger<Z2MApiClient> _logger;
    private readonly IManagedMqttClient _mqtt;
    private readonly IManagedMqttClientOptions _options;
    private readonly CancellationToken _stoppingToken;
    private readonly string _topicPrefix;
    private int _nextMessageId;
    private ConcurrentDictionary<int, TaskCompletionSource<MqttApplicationMessage>> _tasks = new();

    public Z2MApiClient(ILogger<Z2MApiClient> logger, IManagedMqttClient mqtt, IManagedMqttClientOptions options, CancellationToken stoppingToken, string prefix = "zwavejs2mqtt/")
    {
        _logger = logger;
        _mqtt = mqtt;
        _options = options;
        _stoppingToken = stoppingToken;
        _topicPrefix = prefix + "_CLIENTS/ZWAVE_GATEWAY-HomeMQTT/api/";

        _stoppingToken.Register(OnStop);
    }

    private void OnStop()
    {
        _logger.LogDebug("Stopping client");

        ConcurrentDictionary<int, TaskCompletionSource<MqttApplicationMessage>> taskList = Interlocked.Exchange(ref _tasks, null);

        if (taskList != null)
            foreach (TaskCompletionSource<MqttApplicationMessage> completionSource in taskList.Values)
                completionSource.TrySetCanceled(_stoppingToken);
    }

    public async Task Start(CancellationToken token)
    {
        _logger.LogDebug("Starting client");

        if (!_mqtt.IsStarted)
        {
            _logger.LogDebug("Starting MQTT client");

            await _mqtt.StartAsync(_options);
        }

        string subscription = _topicPrefix + "+";
        await _mqtt.SubscribeAsync(subscription);

        _mqtt.AddApplicationMessageReceivedHandler(OnCallback);
    }

    public async Task<Task<MqttApplicationMessage>> SendCommand(string api, object[] args = null, CancellationToken token = default)
    {
        _logger.LogDebug("Sending command {api} with args {args}", api, args);

        string topic = _topicPrefix + api + "/set";
        int messageId = Interlocked.Increment(ref _nextMessageId);

        string requestJson = JsonConvert.SerializeObject(new { messageId, args });

        TaskCompletionSource<MqttApplicationMessage> taskCompletion = new();

        ConcurrentDictionary<int, TaskCompletionSource<MqttApplicationMessage>> localTasks = _tasks;

        if (localTasks == null || _stoppingToken.IsCancellationRequested)
            throw new OperationCanceledException();

        _tasks.TryAdd(messageId, taskCompletion);
        await _mqtt.PublishAsync(topic, requestJson);

        localTasks = Interlocked.CompareExchange(ref _tasks, localTasks, localTasks);
        if (localTasks == null)
        {
            // Was stopped before we noticed
            taskCompletion.TrySetCanceled();
            throw new OperationCanceledException();
        }

        return taskCompletion.Task;
    }

    private Task OnCallback(MqttApplicationMessageReceivedEventArgs arg)
    {
        string topic = arg.ApplicationMessage.Topic;

        _logger.LogTrace("Received callback for topic {topic}", topic);

        if (!topic.StartsWith(_topicPrefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        string payloadString = arg.ApplicationMessage.ConvertPayloadToString();

        _logger.LogDebug("Received callback for topic {topic} with message {message}", topic, payloadString);

        JObject obj = JObject.Parse(payloadString);
        JToken origin = obj["origin"];
        int messageId = origin.Value<int>("messageId");

        if (!_tasks.TryRemove(messageId, out TaskCompletionSource<MqttApplicationMessage> tcs))
            return Task.CompletedTask;

        MqttApplicationMessage message = arg.ApplicationMessage;
        tcs.SetResult(message);

        return Task.CompletedTask;
    }
}