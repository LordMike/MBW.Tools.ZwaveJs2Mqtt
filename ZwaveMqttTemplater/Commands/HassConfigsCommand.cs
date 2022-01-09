using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Mqtt;
using ZwaveMqttTemplater.Z2M;

namespace ZwaveMqttTemplater.Commands;

[Command("hass", "Config management HASS", typeof(Options))]
internal class HassConfigsCommand : CommandBase
{
    private readonly ILogger<HassConfigsCommand> _logger;
    private readonly Options _options;
    private readonly MqttStore _store;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Required]
        [FileExists]
        [Option("-d|--devices", "Device list with specs")]
        public string DeviceFile { get; set; }

        [Option("-p|--hassPrefix", "Home Assistant MQTT prefix. [homeassistant]")]
        public string HassPrefix { get; set; } = "homeassistant";

        [Option("-y|--confirm", "Automatically apply changes")]
        public bool Confirm { get; set; }

        [Option("-v|--verbose", "More details")]
        public bool Verbose { get; set; }
    }

    public HassConfigsCommand(ILogger<HassConfigsCommand> logger, Options options, MqttStore store, IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
        _store = store;
    }

    protected override async Task OnExecuteAsync(CancellationToken token)
    {
        _logger.LogInformation("Managing HASS configs with filter: {Filter}", _options.Filter);

        Z2MApiClient client = await GetApiClient();
        await _store.Load(_options.HassPrefix + "/#");

        HashSet<string> filterNodeNames = null;
        if (!string.IsNullOrEmpty(_options.Filter))
        {
            filterNodeNames = (await CommandHelpers.GetNodesByFilter(client, _options.Filter))
                .Select(s => s.name)
                .Where(s => s != null)
                .ToHashSet(StringComparer.Ordinal);
        }

        IEnumerable<string[]> lines = File.ReadLines(_options.DeviceFile)
            .Select(s => s.Split('\t'))
            .Where(s => s.Length == 2);

        int entities = 0;
        foreach (string[] strings in lines)
        {
            string product = strings[0];
            string nodeName = strings[1];

            if (filterNodeNames != null && !filterNodeNames.Contains(nodeName))
                continue;

            _logger.LogDebug("Preparing {NodeName}, {Product}", nodeName, product);
            entities += ProcessDevice(nodeName, product);
        }

        CommandHelpers.FlushResult flushResult = await CommandHelpers.TopicPromptAndFlush(_logger, _store, _options.Confirm, _options.Verbose);

        _logger.LogInformation("Finished processing HASS configs, evaluated {Count} entities", entities);
        if (flushResult == CommandHelpers.FlushResult.NoTopicsToFlush)
            _logger.LogInformation("There were no changes to make");
    }

    private int ProcessDevice(string nodeName, string product)
    {
        JObject doc = ReadDoc("docs", product);

        // Replace
        doc = (JObject)Transform(doc, token =>
        {
            if (token.Type == JTokenType.String)
                return JToken.FromObject(token.Value<string>().Replace("NODE_NAME", nodeName));

            return token;
        });

        // Read device doc
        doc.Remove("device", out JToken deviceDoc);
        doc.Remove("availability", out JToken availabilityDoc);

        int entities = 0;
        foreach (JProperty typeProp in doc.Properties())
        {
            string type = typeProp.Name;
            JObject discoveryDocs = (JObject)typeProp.Value;

            foreach (KeyValuePair<string, JToken> discoveryDocItem in discoveryDocs)
            {
                string entityName = discoveryDocItem.Key;
                JToken discoveryDoc = discoveryDocItem.Value;
                string discoveryTopic = $"{_options.HassPrefix}/{type}/{nodeName}/{entityName}/config";

                // Add device doc
                if (deviceDoc != null)
                    discoveryDoc["device"] = deviceDoc;

                if (availabilityDoc != null)
                    discoveryDoc["availability"] = availabilityDoc;

                _store.Set(discoveryTopic, JsonConvert.SerializeObject(discoveryDoc), true);
                entities++;
            }
        }

        return entities;
    }

    private static JToken Transform(JToken token, Func<JToken, JToken> action)
    {
        token = action(token);

        if (token is JObject asObj)
        {
            foreach (string key in asObj.Properties().Select(s => s.Name).ToArray())
            {
                JToken item = asObj[key];
                item = Transform(item, action);
                asObj[key] = item;
            }
        }
        else if (token is JArray asArray)
        {
            for (int i = 0; i < asArray.Count; i++)
            {
                JToken item = asArray[i];
                item = Transform(item, action);
                asArray[i] = item;
            }
        }

        return token;
    }

    private static JObject ReadDoc(string kind, string name)
    {
        Type type = typeof(Program);
        Assembly assembly = type.Assembly;

        using Stream strm = assembly.GetManifestResourceStream($"{type.Namespace}.{kind}.{name}.json");
        using StreamReader sr = new(strm, Encoding.UTF8);

        return JObject.Parse(sr.ReadToEnd());
    }
}