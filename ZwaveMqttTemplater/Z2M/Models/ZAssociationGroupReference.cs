namespace ZwaveMqttTemplater.Z2M.Models;

internal class ZAssociationGroupReference
{
    public int Endpoint { get; set; }
    public int GroupId { get; set; }

    public ZAssociationGroupReference(int endpoint, int groupId)
    {
        Endpoint = endpoint;
        GroupId = groupId;
    }

    public override string ToString()
    {
        return $"e{Endpoint}/g{GroupId}";
    }

    public string Render()
    {
        return $"{Endpoint}.{GroupId}";
    }

    public static ZAssociationGroupReference Parse(string value)
    {
        string[] splits = value.Split('.');
        int endpoint = int.Parse(splits[0]);
        int groupId = int.Parse(splits[1]);

        return new ZAssociationGroupReference(endpoint, groupId);
    }
}