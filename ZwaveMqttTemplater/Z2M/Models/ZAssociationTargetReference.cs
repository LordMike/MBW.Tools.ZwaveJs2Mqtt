namespace ZwaveMqttTemplater.Z2M.Models;

internal class ZAssociationTargetReference
{
    public int NodeId { get; set; }
    public int? Endpoint { get; set; }

    public ZAssociationTargetReference(int nodeId, int? endpoint)
    {
        NodeId = nodeId;
        Endpoint = endpoint;
    }

    public override string ToString()
    {
        if (Endpoint == null)
            return $"n{NodeId}/eNONE";

        return $"n{NodeId}/e{Endpoint}";
    }

    public static ZAssociationTargetReference Parse(Z2MNodes nodes, string value)
    {
        string[] splits = value.Split('.');

        int? endpoint = splits[1] == "-" ? null : Convert.ToInt32(splits[1]);

        Z2MNode node = nodes.GetByName(splits[0]).FirstOrDefault();
        if (node != null)
            return new ZAssociationTargetReference(node.id, endpoint);

        node = nodes.GetById(splits[0]);
        if (node != null)
            return new ZAssociationTargetReference(node.id, endpoint);

        throw new Exception("Unable to find target for " + value);
    }

    public string Render() => Render(null, true);

    public string Render(Z2MNodes nodes, bool forceNodeId = false)
    {
        Z2MNode node = forceNodeId ? null : nodes?.GetById(NodeId);

        return (string.IsNullOrEmpty(node?.name) ? NodeId.ToString() : node.name) + "." + (Endpoint?.ToString() ?? "-");
    }
}