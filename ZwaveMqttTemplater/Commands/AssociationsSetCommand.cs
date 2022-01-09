using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.ConfigModels;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Commands;

[Command("associations-set", "Pushes local associations to z-wave", typeof(Options))]
internal class AssociationsSetCommand : CommandBase
{
    private readonly ILogger<AssociationsSetCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Required]
        [FileExists]
        [Option("-f|--file")]
        public string File { get; set; }

        [Option("-r|--refresh", "Refresh configuration first")]
        public bool Refresh { get; set; }

        [Option("--remove-missing", "Remove associations in z-wave, that don't exist in file")]
        public bool RemoveMissing { get; set; }

        [Option("-n|--dry-run")]
        public bool DryRun { get; set; }
    }

    public AssociationsSetCommand(ILogger<AssociationsSetCommand> logger, Options options,  IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Pushing associations with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();

        Z2MNodes nodes = await client.GetNodes();
        List<Z2MNode> selection = CommandHelpers.GetNodesByFilter(nodes, _options.Filter).ToList();

        if (_options.Refresh)
        {
            foreach (Z2MNode node in selection)
            {
                _logger.LogInformation("Refreshing configuration for {Node}", node);
                await client.RefreshCCValues(node.id, CommandClass.AssociationCC);
                await client.RefreshCCValues(node.id, CommandClass.MultiChannelAssociationCC);
            }

            selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);
        }

        if (!File.Exists(_options.File))
            throw new Exception();

        string json = await File.ReadAllTextAsync(_options.File);
        DesiredAssociationsContainer model = JsonConvert.DeserializeObject<DesiredAssociationsContainer>(json);

        foreach (Z2MNode node in selection)
        {
            List<Z2mAssociation> wantedAssociations = new();
            if (model.Nodes.TryGetValue(node.id.ToString(), out DesiredAssociations modelAssociations) ||
                model.Nodes.TryGetValue(node.name, out modelAssociations))
            {
                // Map desired
                foreach ((ZAssociationGroupReference @group, ZAssociationTargetReference target) in modelAssociations.Links.SelectMany(s => s.Value.Select(x => (group: ZAssociationGroupReference.Parse(s.Key), target: ZAssociationTargetReference.Parse(nodes, x)))))
                {
                    wantedAssociations.Add(new Z2mAssociation(group.Endpoint, group.GroupId, target.NodeId, target.Endpoint));
                }
            }

            // Get all associations
            List<Z2mAssociation> associations = await client.GetAssociations(node.id, null);
            Z2mTweaks.AssociationTranslation(node, associations);

            // Prepare actions
            (Z2mAssociation[] toAdd, _, Z2mAssociation[] toRemoveRemote) = SetHelpers.Compare(wantedAssociations, associations, x => $"{x.Endpoint}-{x.GroupId}-{x.NodeId}-{x.TargetEndpoint}");

            if (toAdd.Any())
            {
                foreach (Z2mAssociation association in toAdd)
                    _logger.LogInformation("Node {Node}, adding {Association}", node, association);

                if (!_options.DryRun)
                {
                    foreach (List<Z2mAssociation> byEndpoint in toAdd.GroupBy(s => s.Endpoint).Select(s => s.ToList()))
                    {
                        Z2mAssociation @ref = byEndpoint.First();

                        await client.AddAssociations(new ZAssociationTargetReference(node.id, @ref.Endpoint),
                            @ref.GroupId,
                            byEndpoint.Select(x => x.TargetReference));
                    }
                }
            }

            if (_options.RemoveMissing && toRemoveRemote.Any())
            {
                foreach (Z2mAssociation association in toRemoveRemote)
                    _logger.LogInformation("Node {Node}, removing {Association}", node, association);

                if (!_options.DryRun)
                {
                    foreach (List<Z2mAssociation> byEndpoint in toRemoveRemote.GroupBy(s => s.Endpoint).Select(s => s.ToList()))
                    {
                        Z2mAssociation @ref = byEndpoint.First();

                        await client.RemoveAssociations(new ZAssociationTargetReference(node.id, @ref.Endpoint),
                            @ref.GroupId,
                            byEndpoint.Select(x => x.TargetReference));
                    }
                }
            }
        }
    }
}