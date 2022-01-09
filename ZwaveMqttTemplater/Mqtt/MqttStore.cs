using System.Text;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;

namespace ZwaveMqttTemplater.Mqtt;

internal class MqttStore
{
    private readonly IManagedMqttClient _client;
    private readonly Dictionary<string, byte[]> _existingTopics;
    private readonly Dictionary<string, (byte[] payload, bool retain)> _desiredTopics;
    private readonly List<(string topic, byte[] payload)> _blindPublish;

    public MqttStore(IManagedMqttClient client)
    {
        _client = client;
        _existingTopics = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        _desiredTopics = new Dictionary<string, (byte[] payload, bool retain)>(StringComparer.Ordinal);
        _blindPublish = new List<(string topic, byte[] payload)>();
    }

    public async Task Load(params string[] topics)
    {
        ManualResetEvent stopEvent = new(false);
        Timer timer = new(_ => stopEvent.Set());

        Task Handler(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            _existingTopics[eventArgs.ApplicationMessage.Topic] = eventArgs.ApplicationMessage.Payload;
            timer.Change(1000, Timeout.Infinite);

            return Task.CompletedTask;
        }

        _client.AddApplicationMessageReceivedHandler(Handler);

        MqttTopicFilter[] topicFilters;
        if (topics.Any())
            topicFilters = topics.Select(s => new MqttTopicFilter
            {
                Topic = s,
                RetainHandling = MqttRetainHandling.SendAtSubscribe,
                QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
            }).ToArray();
        else
            topicFilters = new[]
            {
                new MqttTopicFilter
                {
                    Topic = "#",
                    RetainHandling = MqttRetainHandling.SendAtSubscribe,
                    QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
                }
            };

        timer.Change(2500, Timeout.Infinite);
        await _client.SubscribeAsync(topicFilters);

        stopEvent.WaitOne();

        await _client.UnsubscribeAsync(topicFilters.Select(s => s.Topic).ToArray());
        _client.RemoveApplicationMessageReceivedHandler(Handler);
    }

    public void Set(string topic, byte[] payload, bool retain = false, byte[] currentValue = null)
    {
        _desiredTopics[topic] = (payload, retain);

        if (currentValue != null)
            _existingTopics[topic] = currentValue;
    }

    public void SetBlindly(string topic, byte[] payload)
    {
        _blindPublish.Add((topic, payload));
    }

    public bool TryGet(string topic, out byte[] payload, bool includeDesired = false)
    {
        if (includeDesired && _desiredTopics.TryGetValue(topic, out (byte[] payload, bool retain) item))
        {
            payload = item.payload;
            return true;
        }

        return _existingTopics.TryGetValue(topic, out payload);
    }

    public string GetCurrentValue(string topic)
    {
        if (!TryGet(topic, out byte[] payload))
            return null;

        return Encoding.UTF8.GetString(payload);
    }

    public string GetSetValue(string topic)
    {
        return Encoding.UTF8.GetString(_desiredTopics[topic].payload);
    }

    public IEnumerable<string> GetTopicsToSet()
    {
        return GetTopicsToSet(true);
    }

    private IEnumerable<string> GetTopicsToSet(bool includeBlind)
    {
        foreach ((string topic, (byte[] payload, bool retain) value) in _desiredTopics)
        {
            if (!_existingTopics.TryGetValue(topic, out byte[] existing) || !existing.SequenceEqual(value.payload))
                yield return topic;
        }

        if (includeBlind)
        {
            foreach ((string topic, byte[] _) in _blindPublish)
                yield return topic;
        }
    }

    public async Task FlushTopicsToSet(bool delay = false)
    {
        foreach (string topic in GetTopicsToSet(false))
        {
            (byte[] payload, bool retain) = _desiredTopics[topic];

            await _client.PublishAsync(new MqttApplicationMessage
            {
                Topic = topic,
                Payload = payload,
                Retain = retain
            });

            if (delay)
                await Task.Delay(400);
        }

        foreach ((string topic, byte[] payload) in _blindPublish)
        {
            await _client.PublishAsync(new MqttApplicationMessage
            {
                Topic = topic,
                Payload = payload
            });
        }
    }
}