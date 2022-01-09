using ZwaveMqttTemplater.CommandSystem;

namespace ZwaveMqttTemplater.Commands.Generic;

[AttributeUsage(AttributeTargets.Property)]
internal class FilterArgumentAttribute : ArgumentAttribute
{
    public FilterArgumentAttribute() : base("filter", "Filter to use to reduce the devices affected by this command")
    {

    }
}