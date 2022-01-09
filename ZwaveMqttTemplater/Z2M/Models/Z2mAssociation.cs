namespace ZwaveMqttTemplater.Z2M.Models;

internal class Z2mAssociation : IEquatable<Z2mAssociation>
{
    public int NodeId { get; set; }

    public int Endpoint { get; set; }

    public int GroupId { get; set; }

    public int? TargetEndpoint { get; set; }

    public ZAssociationGroupReference GroupReference => new(Endpoint, GroupId);
    public ZAssociationTargetReference TargetReference => new(NodeId, TargetEndpoint);

    public Z2mAssociation(int endpoint, int groupId, int nodeId, int? targetEndpoint)
    {
        NodeId = nodeId;
        Endpoint = endpoint;
        GroupId = groupId;
        TargetEndpoint = targetEndpoint;
    }

    public bool Equals(Z2mAssociation other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return NodeId == other.NodeId && Endpoint == other.Endpoint;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((Z2mAssociation)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(NodeId, Endpoint);
    }

    public static bool operator ==(Z2mAssociation left, Z2mAssociation right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(Z2mAssociation left, Z2mAssociation right)
    {
        return !Equals(left, right);
    }

    public override string ToString()
    {
        return $"e{Endpoint}/g{GroupId} -> n{NodeId}/e{TargetEndpoint}";
    }
}