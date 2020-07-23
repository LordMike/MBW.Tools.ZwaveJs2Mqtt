using System;

namespace ZwaveMqttTemplater
{
    class ValueKey : IEquatable<ValueKey>
    {
        public int NodeId { get; set; }

        public int Class { get; set; }

        public int Instance { get; set; }

        public int Index { get; set; }

        public override string ToString()
        {
            return $"{NodeId}-{Class}-{Instance}-{Index}";
        }

        public bool Equals(ValueKey other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return NodeId == other.NodeId && Class == other.Class && Instance == other.Instance && Index == other.Index;
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
            return HashCode.Combine(NodeId, Instance, Index);
        }
    }
}