using Microsoft.Extensions.Logging;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Commands;

[Command("list-fw", "Export configs in csv format", typeof(Options))]
internal class ListFirmwaresCommand : CommandBase
{
    private readonly ILogger<ListFirmwaresCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }
    }

    public ListFirmwaresCommand(ILogger<ListFirmwaresCommand> logger, Options options,  IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected async override Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Listing firmwares with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();
        List<Z2MNode> selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);

        IOrderedEnumerable<IGrouping<string, Z2MNode>> sorted = selection
            .GroupBy(s => s.manufacturerId + "-" + s.productId + "-" + s.firmwareVersion)
            .OrderBy(s => s.Key);

        foreach (IGrouping<string, Z2MNode> grp in sorted)
        {
            List<IGrouping<string, Z2MNode>> byFirmware = grp.GroupBy(s => s.firmwareVersion).ToList();

            if (byFirmware.Count != 1)
                _logger.LogWarning("Multiple firmwares in use for {Product}", grp.First().productLabel);

            foreach (IGrouping<string, Z2MNode> fwNodes in byFirmware)
            {
                Z2MNode node = fwNodes.First();
                _logger.LogInformation("{Product} by {Manufacturer} fw {Firmwre} is used by {Nodes}", node.productLabel, node.manufacturer, fwNodes.Key, fwNodes);
            }
        }
    }
}