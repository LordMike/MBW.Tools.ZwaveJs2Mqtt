using System.Text;

namespace ZwaveMqttTemplater
{
    static class Extensions
    {
        public static void Set(this MqttStore store, string topic, string payload, bool retain = false, string compareTopic = null)
        {
            store.Set(topic, Encoding.UTF8.GetBytes(payload), retain, compareTopic);
        }
        
        public static void SetBlindly(this MqttStore store, string topic, string payload)
        {
            store.SetBlindly(topic, Encoding.UTF8.GetBytes(payload));
        }

        public static bool TryGetString(this MqttStore store, string topic, out string payload)
        {
            payload = default;
            if (!store.TryGet(topic, out byte[] bytes))
                return false;

            payload = Encoding.UTF8.GetString(bytes);
            return true;
        }
    }
}