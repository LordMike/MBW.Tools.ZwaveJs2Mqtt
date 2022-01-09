using System.Diagnostics;
using System.Runtime.Serialization;
using EnumsNET;
using Microsoft.Extensions.Logging;
using MQTTnet;
using Newtonsoft.Json.Linq;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Commands;

[Command("do", "Mass execute API calls", typeof(Options))]
internal class DoCommand : CommandBase
{
    private readonly ILogger<DoCommand> _logger;
    private readonly Options _options;

    internal class Options : OptionsBase
    {
        [FilterArgument]
        public string Filter { get; set; }

        [Option("-o|--operation", "API Operation")]
        public OperationKind Operation { get; set; }

        [Option("--arg", "Additional arguments for operation")]
        public string[] ExtraArgs { get; set; }

        [Option("-n|--dry-run")]
        public bool DryRun { get; set; }
    }

    public DoCommand(ILogger<DoCommand> logger, Options options,  IServiceProvider serviceProvider) : base(serviceProvider)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task OnExecuteAsync(CancellationToken token)
    {
        string? apiName = _options.Operation.AsString(EnumFormat.EnumMemberValue);

        _logger.LogInformation("Performing {CommandClasses} with filter: {Filter}", _options.Operation, _options.Filter);

        Z2MApiClient client = await GetApiClient();

        List<Z2MNode> selection = await CommandHelpers.GetNodesByFilter(client, _options.Filter);

        _logger.LogInformation("Performing {CommandClasses} on {Count} nodes", _options.Operation, selection.Count);

        Stopwatch sw = new();

        foreach (Z2MNode node in selection)
        {
            _logger.LogInformation("Performing {CommandClasses} on {Node}", _options.Operation, node);
            object[] args = { node.id };
            if (_options.ExtraArgs != null)
                args = args.Append(_options.ExtraArgs).ToArray();

            if (_options.DryRun)
            {
                _logger.LogInformation("Dry run, not performing {Action} w/ {Arguments}", apiName, args);
            }
            else
            {
                sw.Restart();
                Task<MqttApplicationMessage> tsk = await client.SendCommand(apiName, args);
                MqttApplicationMessage res = await tsk;
                sw.Stop();

                string json = res.ConvertPayloadToString();
                JObject resObj = JObject.Parse(json);
                string resMessage = resObj.Value<string>("message");

                _logger.LogInformation("Took {TimeTaken:N0}ms: {Message}", sw.ElapsedMilliseconds, resMessage);
            }
        }
    }

    public enum OperationKind
    {
        [EnumMember(Value = "pingNode")]
        Ping,

        [EnumMember(Value = "healNode")]
        Heal
    }
}