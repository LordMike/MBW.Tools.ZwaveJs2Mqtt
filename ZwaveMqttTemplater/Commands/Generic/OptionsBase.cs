using ZwaveMqttTemplater.CommandSystem;

namespace ZwaveMqttTemplater.Commands.Generic;

internal class OptionsBase
{
    [Option("-h|--help")]
    public bool Help { get; set; }
}