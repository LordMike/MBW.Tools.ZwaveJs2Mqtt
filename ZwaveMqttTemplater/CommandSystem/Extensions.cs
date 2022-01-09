using ZwaveMqttTemplater.Commands.Generic;

namespace ZwaveMqttTemplater.CommandSystem;

internal static class Extensions
{
    public static bool IsHelpRequested(this IDictionary<string, string> cmdValues)
    {
        const string HelpName = nameof(OptionsBase.Help);

        KeyValuePair<string, string> value = cmdValues.FirstOrDefault(s => s.Key.Equals(HelpName) || s.Key.EndsWith(":" + HelpName));

        return value.Value == true.ToString();
    }
}