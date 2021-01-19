using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace ZwaveMqttTemplater
{
    class MqttStore
    {
        private readonly IMqttClient _client;
        private readonly Dictionary<string, byte[]> _existingTopics;
        private readonly Dictionary<string, (byte[] payload, bool retain)> _desiredTopics;
        private readonly List<(string topic, byte[] payload)> _blindPublish;

        public MqttStore(IMqttClient client)
        {
            _client = client;
            _existingTopics = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            _desiredTopics = new Dictionary<string, (byte[] payload, bool retain)>(StringComparer.Ordinal);
            _blindPublish = new List<(string topic, byte[] payload)>();
        }

        public async Task Load(params string[] topics)
        {
            ManualResetEvent stopEvent = new ManualResetEvent(false);
            Timer timer = new Timer(state => stopEvent.Set());
            timer.Change(2500, Timeout.Infinite);

            _client.UseApplicationMessageReceivedHandler(eventArgs =>
            {
                _existingTopics[eventArgs.ApplicationMessage.Topic] = eventArgs.ApplicationMessage.Payload;
                timer.Change(1000, Timeout.Infinite);
            });

            TopicFilter[] topicFilters;
            if (topics.Any())
                topicFilters = topics.Select(s => new TopicFilter { Topic = s }).ToArray();
            else
                topicFilters = new[]
                {
                    new TopicFilter
                    {
                        Topic = "#"
                    }
                };

            await _client.SubscribeAsync(topicFilters);

            stopEvent.WaitOne();

            await _client.UnsubscribeAsync(topicFilters.Select(s => s.Topic).ToArray());
            _client.ApplicationMessageReceivedHandler = null;
        }

        public void Set(string topic, byte[] payload, bool retain = false)
        {
            _desiredTopics[topic] = (payload, retain);
        }

        public void SetBlindly(string topic, byte[] payload)
        {
            _blindPublish.Add((topic, payload));
        }

        public bool TryGet(string topic, out byte[] payload)
        {
            if (_desiredTopics.TryGetValue(topic, out (byte[] payload, bool retain) item))
            {
                payload = item.payload;
                return true;
            }

            return _existingTopics.TryGetValue(topic, out payload);
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

        public async Task FlushTopicsToSet()
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