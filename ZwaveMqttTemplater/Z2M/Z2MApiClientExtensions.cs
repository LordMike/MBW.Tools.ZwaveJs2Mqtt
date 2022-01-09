using MQTTnet;
using Newtonsoft.Json;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Z2M;

internal static class Z2MApiClientExtensions
{
    private static async Task<TResult> InvokeJson<TResult>(this Z2MApiClient client, string api, object[] args = null)
    {
        Task<MqttApplicationMessage> tsk = await client.SendCommand(api, args);
        MqttApplicationMessage res = await tsk;

        string json = res.ConvertPayloadToString();

        Z2MApiCallResult<TResult> result = JsonConvert.DeserializeObject<Z2MApiCallResult<TResult>>(json);

        if (!result.Success)
            throw new Exception("");

        return result.Result;
    }

    public static async Task<Z2MNodes> GetNodes(this Z2MApiClient client)
    {
        return new Z2MNodes(await client.InvokeJson<List<Z2MNode>>("getNodes"));
    }

    public static async Task PingNode(this Z2MApiClient client, int nodeId)
    {
        await await client.SendCommand("pingNode", new object[] { nodeId });
    }

    public static async Task HealNode(this Z2MApiClient client, int nodeId)
    {
        await await client.SendCommand("healNode", new object[] { nodeId });
    }

    public static async Task RefreshCCValues(this Z2MApiClient client, int nodeId, CommandClass commandClass)
    {
        await await client.SendCommand("refreshCCValues", new object[] { nodeId, commandClass });
    }

    public static async Task<List<Z2mAssociation>> GetAssociations(this Z2MApiClient client, int nodeId, int? groupId)
    {
        return await client.InvokeJson<List<Z2mAssociation>>("getAssociations", new object[] { nodeId, groupId });
    }

    public static async Task AddAssociations(this Z2MApiClient client, ZAssociationTargetReference source, int? groupId, IEnumerable<ZAssociationTargetReference> targets)
    {
        await client.SendCommand("addAssociations", new object[]
        {
            new {
                nodeId = source.NodeId,
                endpoint = source.Endpoint
            },
            groupId,
            targets.Select(s=>new
            {
                nodeId = s.NodeId,
                endpoint = s.Endpoint
            })
        });
    }

    public static async Task RemoveAssociations(this Z2MApiClient client, ZAssociationTargetReference source, int? groupId, IEnumerable<ZAssociationTargetReference> targets)
    {
        await client.SendCommand("removeAssociations", new object[]
        {
            new {
                nodeId = source.NodeId,
                endpoint = source.Endpoint
            },
            groupId,
            targets.Select(s=>new
            {
                nodeId = s.NodeId,
                endpoint = s.Endpoint
            })
        });
    }
}