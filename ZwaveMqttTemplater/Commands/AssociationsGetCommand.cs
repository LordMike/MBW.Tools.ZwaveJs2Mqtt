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

[Command("associations-get", "Export current associations to file", typeof(Options))]
internal class AssociationsGetCommand : CommandBase
{
    private readonly ILogger<AssociationsGetCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Required]
        //[LegalFilePath]
        [Option("-f|--file")]
        public string File { get; set; }

        [Option("-r|--refresh", "Refresh configuration first")]
        public bool Refresh { get; set; }

        [Option("--remove-missing", "Remove associations in file, that don't exist in Z-wave")]
        public bool RemoveMissing { get; set; }
    }

    public AssociationsGetCommand(ILogger<AssociationsGetCommand> logger, Options options, IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Dumping associations with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();

        Z2MNodes nodes = await client.GetNodes();
        List<Z2MNode> selection = CommandHelpers.GetNodesByFilter(nodes, _options.Filter).ToList();

        if (_options.Refresh)
        {
            foreach (Z2MNode node in selection)
            {
                _logger.LogInformation("Refreshing configuration for {Node}", node);
                await client.RefreshCCValues(node.id, CommandClass.AssociationCC);
            }

            selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);
        }

        DesiredAssociationsContainer model = new();
        if (File.Exists(_options.File))
        {
            string json = await File.ReadAllTextAsync(_options.File);
            model = JsonConvert.DeserializeObject<DesiredAssociationsContainer>(json);
        }

        foreach (Z2MNode node in selection)
        {
            if (model.Nodes.TryGetValue(node.id.ToString(), out DesiredAssociations modelAssociations))
            {
                if (!string.IsNullOrEmpty(node.name))
                {
                    // Replace with name
                    model.Nodes.Remove(node.id.ToString());
                    model.Nodes[node.name] = modelAssociations;
                }
            }
            else if (!model.Nodes.TryGetValue(node.name, out modelAssociations))
            {
                // Create new
                modelAssociations = new DesiredAssociations();
                model.Nodes[node.NameOrId] = modelAssociations;
            }

            // Prepare model assocations
            HashSet<string> localMissingAssociations = modelAssociations.Links.SelectMany(s => s.Value.Select(x => GetLocalDedupId(s.Key, x))).ToHashSet(StringComparer.Ordinal);

            // Get all associations
            List<Z2mAssociation> associations = await client.GetAssociations(node.id, null);
            Z2mTweaks.AssociationTranslation(node, associations);

            foreach (Z2mAssociation currentAssociation in associations)
            {
                string groupRef = currentAssociation.GroupReference.Render();
                string targetRef = currentAssociation.TargetReference.Render();
                string targetRefWithName = currentAssociation.TargetReference.Render(nodes);

                if (!modelAssociations.Links.TryGetValue(groupRef, out HashSet<string> targets))
                    modelAssociations.Links[groupRef] = targets = new HashSet<string>(StringComparer.Ordinal);

                targets.Remove(targetRef);
                targets.Add(targetRefWithName);

                localMissingAssociations.Remove(GetLocalDedupId(groupRef, targetRef));
                localMissingAssociations.Remove(GetLocalDedupId(groupRef, targetRefWithName));
            }

            if (_options.RemoveMissing && localMissingAssociations.Any())
            {
                foreach ((string endpointGroup, HashSet<string> targets) in modelAssociations.Links)
                    targets.RemoveAll(s => localMissingAssociations.Contains(GetLocalDedupId(endpointGroup, s)));
            }

            // Cleanup groups with no targets
            modelAssociations.Links.RemoveAll((_, value) => value.Count == 0);
        }

        // Cleanup nodes with empty link lists
        model.Nodes.RemoveAll((_, value) => value.Links.Count == 0);

        {
            string json = JsonConvert.SerializeObject(model, Formatting.Indented);
            await File.WriteAllTextAsync(_options.File, json);
        }
    }

    private string GetLocalDedupId(string groupRef, string targetRef)
    {
        return groupRef + "::" + targetRef;
    }
}