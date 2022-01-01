using System;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client.Receiving;
using MQTTnet.Extensions.ManagedClient;

namespace ZwaveMqttTemplater.Z2M;

static class MqttHelpers
{
    public static void AddApplicationMessageReceivedHandler(this IManagedMqttClient client, Func<MqttApplicationMessageReceivedEventArgs, Task> newHandler)
    {
        RollingDelegateHandler handler = client.ApplicationMessageReceivedHandler as RollingDelegateHandler;
        if (handler == null)
            client.ApplicationMessageReceivedHandler = handler = new RollingDelegateHandler();

        handler.OnMessage += newHandler;
    }

    public static void RemoveApplicationMessageReceivedHandler(this IManagedMqttClient client, Func<MqttApplicationMessageReceivedEventArgs, Task> newHandler)
    {
        RollingDelegateHandler handler = client.ApplicationMessageReceivedHandler as RollingDelegateHandler;
        if (handler == null)
            return;

        handler.OnMessage -= newHandler;
    }

    class RollingDelegateHandler : IMqttApplicationMessageReceivedHandler
    {
        public event Func<MqttApplicationMessageReceivedEventArgs, Task> OnMessage;

        public async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            Func<MqttApplicationMessageReceivedEventArgs, Task> eventHandler = OnMessage;
            if (eventHandler == null)
                return;

            Delegate[] handlers = eventHandler.GetInvocationList();

            foreach (Delegate handler in handlers)
            {
                Func<MqttApplicationMessageReceivedEventArgs, Task> asFunc = (Func<MqttApplicationMessageReceivedEventArgs, Task>)handler;
                Task task = asFunc(eventArgs);

                await task;
            }
        }
    }
}