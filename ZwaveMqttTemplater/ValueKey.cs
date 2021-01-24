using System;

namespace ZwaveMqttTemplater
{
    class ValueKey : IEquatable<ValueKey>
    {
        public int NodeId { get; }

        public string Key { get; }

        public ValueKey(int nodeId, string key)
        {
            NodeId = nodeId;
            Key = key;
        }

        public override string ToString()
        {
            return $"{NodeId}-{Key}";
        }

        public bool Equals(ValueKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return NodeId == other.NodeId && Key == other.Key;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ValueKey)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(NodeId, Key);
        }
    }
}