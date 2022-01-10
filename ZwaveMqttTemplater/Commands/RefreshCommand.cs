using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Commands;

[Command("refresh", "Refresh values", typeof(Options))]
internal class RefreshCommand : CommandBase
{
    private readonly ILogger<RefreshCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Option("-c|--commandClass", "Command classes")]
        public CommandClass[] CommandClasses { get; set; }

        [Option("-a|--all")]
        public bool AllClasses { get; set; }

        [Option("-n|--dry-run")]
        public bool DryRun { get; set; }
    }

    public RefreshCommand(ILogger<RefreshCommand> logger, Options options, IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected async override Task OnExecuteAsync(CancellationToken token)
    {
        object ccLogSelection = _options.AllClasses ? "all" : _options.CommandClasses;
        _logger.LogInformation("Refreshing {CommandClasses} with filter: {Filter}", ccLogSelection, _options.Filter);

        Z2MApiClient client = await GetApiClient();
        List<Z2MNode> selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);

        _logger.LogInformation("Refreshing {CommandClasses} on {Count} nodes", ccLogSelection, selection.Count);

        Stopwatch sw = new();

        foreach (Z2MNode node in selection)
        {
            if (_options.DryRun)
            {
                _logger.LogInformation("Dry run, not refreshing {CommandClasses} on node {NodeId}", _options.CommandClasses, node.id);
            }
            else
            {
                sw.Restart();

                CommandClass[] classes = _options.CommandClasses;
                if (_options.AllClasses)
                    classes = node.values.Select(s => (CommandClass)s.Value.commandClass).Distinct().ToArray();

                _logger.LogInformation("Refreshing {CommandClasses} on {Node}", classes, node);

                foreach (CommandClass commandClass in classes)
                    await client.RefreshCCValues(node.id, commandClass);
                sw.Stop();

                _logger.LogInformation("Took {TimeTaken:N0}ms", sw.ElapsedMilliseconds);
            }
        }
    }

}