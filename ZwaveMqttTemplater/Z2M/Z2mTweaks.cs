using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Z2M;

internal static class Z2mTweaks
{
    public static void AssociationTranslation(Z2MNode node, IList<Z2mAssociation> associations)
    {
        // ZDB5100 maps endpoint groups to groups in endpoint 0.
        // So we hide all associations in endpoint 0, groups >=2; AND lifeline groups in other endpoints
        if (node.productLabel == "ZDB5100")
        {
            associations.RemoveAll(s => (s.Endpoint == 0 && s.GroupId >= 2) ||
                                        (s.Endpoint != 0 && s.GroupId == 1));
        }
    }
}