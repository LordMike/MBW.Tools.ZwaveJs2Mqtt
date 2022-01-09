using Serilog.Events;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;

namespace ZwaveMqttTemplater.Commands;

[Command("root", optionsType: typeof(Options))]
internal class RootCommand : CommandBase
{
    private readonly CommandLineHelper<CommandBase> _cmdHelper;

    internal class Options : OptionsBase
    {
        [Option("-m|--mqtt", "Mqtt Host to use. Default: queue.home")]
        public string MqttHost { get; set; } = "queue.home";

        [Option("-p|--port", "Mqtt Port to use. Default: 1883")]
        public ushort MqttPort { get; set; } = 1883;

        [Option("-l|--log-level", "Log level. Default: Information")]
        public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;
    }

    public RootCommand(CommandLineHelper<CommandBase> cmdHelper, IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _cmdHelper = cmdHelper;
    }

    protected override async Task OnExecuteAsync(CancellationToken token)
    {
        _cmdHelper.PrintHelp(Console.Out, GetType());

        Console.WriteLine();
        Console.WriteLine("Many commands accept a <filter> argument that can be used to reduce the number of devices affected by a command.");

        Console.WriteLine();
        Console.WriteLine("Valid filters are:");
        Console.WriteLine("name:my_device       Filter by name");
        Console.WriteLine("product:ZWA025       Filter by product name");
        Console.WriteLine("manufacturer:aeotec  Filter by product manufacturer");
        Console.WriteLine("id:25                Filter by z-wave node id");
        Console.WriteLine("flag:awake           Filter by z-wave awake devices");
        Console.WriteLine("flag:asleep          Filter by z-wave asleep devices");
        Console.WriteLine();
    }
}