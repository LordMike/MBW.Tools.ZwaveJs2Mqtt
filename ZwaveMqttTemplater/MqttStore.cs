using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Extensions.ManagedClient;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater
{
    class MqttStore
    {
        private readonly IManagedMqttClient _client;
        private readonly Dictionary<string, byte[]> _existingTopics;
        private readonly Dictionary<string, (byte[] payload, bool retain, string compareTopic)> _desiredTopics;
        private readonly List<(string topic, byte[] payload)> _blindPublish;

        public MqttStore(IManagedMqttClient client)
        {
            _client = client;
            _existingTopics = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _desiredTopics = new Dictionary<string, (byte[] payload, bool retain, string compareTopic)>(StringComparer.Ordinal);
            _blindPublish = new List<(string topic, byte[] payload)>();
        }

        public async Task Load(params string[] topics)
        {
            ManualResetEvent stopEvent = new ManualResetEvent(false);
            Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(2500, Timeout.Infinite);

            Task Handler(MqttApplicationMessageReceivedEventArgs eventArgs)
            {
                _existingTopics[eventArgs.ApplicationMessage.Topic] = eventArgs.ApplicationMessage.Payload;
                timer.Change(1000, Timeout.Infinite);

                return Task.CompletedTask;
            }

            _client.AddApplicationMessageReceivedHandler(Handler);

            MqttTopicFilter[] topicFilters;
            if (topics.Any())
                topicFilters = topics.Select(s => new MqttTopicFilter { Topic = s }).ToArray();
            else
                topicFilters = new[]
                {
                    new MqttTopicFilter
                    {
                        Topic = "#"
                    }
                };

            await _client.SubscribeAsync(topicFilters);

            stopEvent.WaitOne();

            await _client.UnsubscribeAsync(topicFilters.Select(s => s.Topic).ToArray());
            _client.RemoveApplicationMessageReceivedHandler(Handler);
        }

        public void Set(string topic, byte[] payload, bool retain = false, string compareTopic = null)
        {
            _desiredTopics[topic] = (payload, retain, compareTopic ?? topic);
        }

        public void SetBlindly(string topic, byte[] payload)
        {
            _blindPublish.Add((topic, payload));
        }

        public bool TryGet(string topic, out byte[] payload)
        {
            //if (_desiredTopics.TryGetValue(topic, out (byte[] payload, bool retain, string compareTopic) item))
            //{
            //    payload = item.payload;
            //    return true;
            //}

            return _existingTopics.TryGetValue(topic, out payload);
        }

        public string GetCurrentValue(string topic)
        {
            if (!TryGet(topic, out var payload))
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
            foreach ((string topic, (byte[] payload, bool retain, string compareTopic) value) in _desiredTopics)
            {
                if (!_existingTopics.TryGetValue(value.compareTopic, out byte[] existing) || !existing.SequenceEqual(value.payload))
                    yield return topic;
            }

            if (includeBlind)
            {
                foreach ((string topic, byte[] _) in _blindPublish)
                    yield return topic;
            }
        }

        public async Task FlushTopicsToSet()
        {
            foreach (string topic in GetTopicsToSet(false))
            {
                (byte[] payload, bool retain, string compareTopic) = _desiredTopics[topic];

                await _client.PublishAsync(new MqttApplicationMessage
                {
                    Topic = topic,
                    Payload = payload,
                    Retain = retain
                });

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
}