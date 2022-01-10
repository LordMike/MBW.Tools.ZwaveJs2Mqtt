using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Mqtt;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;
using ConfigDict = System.Collections.Generic.Dictionary<string, (int nodeId, string key, object value, object currentValue)>;

namespace ZwaveMqttTemplater.Commands;

[Command("device-configs", "Config management devices", typeof(Options))]
internal class DeviceConfigsCommand : CommandBase
{
    private readonly static Regex LineRegex = new(@"^([^#\t]+)\t([^\t]+)\t([^\t ]+)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ILogger<DeviceConfigsCommand> _logger;
    private readonly Options _options;
    private readonly MqttStore _store;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Required]
        [FileExists]
        [Option("-f|--file", "Device config file")]
        public string File { get; set; }

        [Option("-y|--confirm", "Automatically apply changes")]
        public bool Confirm { get; set; }

        [Option("-v|--verbose", "More details")]
        public bool Verbose { get; set; }

    }

    public DeviceConfigsCommand(ILogger<DeviceConfigsCommand> logger, Options options, MqttStore store, IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
        _store = store;
    }

    protected async override Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Managing device configs with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();
        Z2MNodes nodes = await client.GetNodes();

        HashSet<string> filterNodeNames = null;
        if (!string.IsNullOrEmpty(_options.Filter))
        {
            filterNodeNames = CommandHelpers.GetNodesByFilter(nodes, _options.Filter)
                .Select(s => s.name)
                .Where(s => s != null)
                .ToHashSet(StringComparer.Ordinal);
        }

        IEnumerable<string[]> lines = File.ReadLines(_options.File)
            .Select(s => LineRegex.Match(s))
            .Where(s => s.Success)
            .Select(s => new[] { s.Groups[1].Value, s.Groups[2].Value, s.Groups[3].Value });

        ConfigDict configMap = new(StringComparer.Ordinal);

        int configLinesEvaluated = 0;
        HashSet<int> devicesEvaluated = new();
        foreach (string[] strings in lines)
        {
            string filter = strings[0];
            string setting = strings[1];
            string value = strings[2];

            List<Z2MNode> applicableNodes = CommandHelpers.GetNodesByFilter(nodes, filter)
                .Where(s => filterNodeNames == null || filterNodeNames.Contains(s.name))
                .ToList();

            if (applicableNodes.Any())
            {
                applicableNodes.ForEach(x => devicesEvaluated.Add(x.id));
                configLinesEvaluated++;

                ProcessDeviceConfigLine(configMap, applicableNodes, setting, value);
            }
        }

        // Move to store
        // > zwavejs2mqtt/wallswitch_4/112/0/2
        foreach ((int nodeId, string key, object value, object currentValue) in configMap.Values)
        {
            string topic = GetSetTopic(nodes.GetById(nodeId), key);
            _store.Set(topic, JsonConvert.SerializeObject(value), existingValue: JsonConvert.SerializeObject(currentValue));
        }

        CommandHelpers.FlushResult flushResult = await CommandHelpers.TopicPromptAndFlush(_logger, _store, _options.Confirm, _options.Verbose, true);

        _logger.LogInformation("Finished processing device configs, evaluated {Count} lines against {Entities} devices", configLinesEvaluated, devicesEvaluated.Count);
        if (flushResult == CommandHelpers.FlushResult.NoTopicsToFlush)
            _logger.LogInformation("There were no changes to make");
    }

    private static void ProcessDeviceConfigLine(ConfigDict desiredConfig, IEnumerable<Z2MNode> nodes, string configKey, string value)
    {
        foreach (Z2MNode node in nodes)
        {
            if (!node.values.TryGetValue(configKey, out Z2MValue existingVal))
                throw new Exception($"Node {node} did not support {configKey}");

            object newVal = ConvertZ2MValue(existingVal, value);
            desiredConfig[GetDesiredKey(node.id, configKey)] = (node.id, configKey, newVal, existingVal.value);
        }
    }

    private static object ConvertZ2MValue(Z2MValue spec, string val = null)
    {
        Z2MState state;
        if (spec.type == "number" &&
            spec.list &&
            (state = spec.states.FirstOrDefault(s => s.text == val)) != null)
        {
            return Convert.ChangeType(state.value, typeof(long));
        }

        if (spec.type == "number")
        {
            return Convert.ToInt64(val);
        }

        throw new Exception();
    }

    private string GetSetTopic(Z2MNode node, string key)
    {
        return $"zwavejs2mqtt/{node.name}/{key.Replace("-", "/")}/set";
    }

    private static string GetDesiredKey(int nodeId, string configKey)
    {
        return nodeId + "-" + configKey;
    }
}