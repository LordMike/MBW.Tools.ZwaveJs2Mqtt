using Microsoft.Extensions.Logging;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Commands;

[Command("dump-config", "Export configs in csv format", typeof(Options))]
internal class DumpConfigCommand : CommandBase
{
    private readonly ILogger<DumpConfigCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Option("-r|--refresh", "Refresh configuration first")]
        public bool Refresh { get; set; }
    }

    public DumpConfigCommand(ILogger<DumpConfigCommand> logger, Options options,  IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected async override Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Dumping configs with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();
        List<Z2MNode> selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);

        if (_options.Refresh)
        {
            foreach (Z2MNode node in selection)
            {
                _logger.LogInformation("Refreshing configuration for {Node}", node);
                await client.RefreshCCValues(node.id, CommandClass.ConfigurationCC);
            }

            selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);
        }

        foreach (Z2MNode node in selection)
        {
            string prefix =
                $"{node.id}\t{node.name}\t{node.loc}\t{node.productLabel} {node.productDescription}\t{node.firmwareVersion}";

            foreach ((string key, Z2MValue value) in node.values)
            {
                if (value.commandClass != 112)
                    continue;

                Console.WriteLine($"{prefix}\t{key}\t{value.value}\t{value.label}");
            }
        }
    }
}