using System.Text;

namespace ZwaveMqttTemplater.Mqtt;

internal static class MqttExtensions
{
    public static void Set(this MqttStore store, string topic, string payload, bool retain = false, string existingValue = null)
    {
        store.Set(topic, Encoding.UTF8.GetBytes(payload), retain, existingValue != null ? Encoding.UTF8.GetBytes(existingValue) : null);
    }

    public static void SetBlindly(this MqttStore store, string topic, string payload)
    {
        store.SetBlindly(topic, Encoding.UTF8.GetBytes(payload));
    }

    public static bool TryGetString(this MqttStore store, string topic, out string payload, bool includeDesired = false)
    {
        payload = default;
        if (!store.TryGet(topic, out byte[] bytes, includeDesired))
            return false;

        payload = Encoding.UTF8.GetString(bytes);
        return true;
    }
}