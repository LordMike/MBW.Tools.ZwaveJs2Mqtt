#nullable enable
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client.Options;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Formatter;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using ZwaveMqttTemplater.Commands;
using ZwaveMqttTemplater.Commands.Generic;
using ZwaveMqttTemplater.CommandSystem;
using ZwaveMqttTemplater.Helpers;
using ZwaveMqttTemplater.Mqtt;
using ZwaveMqttTemplater.Z2M;
using ILogger = Serilog.ILogger;

namespace ZwaveMqttTemplater;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        CommandLineHelper<CommandBase> cmdLineHelper = new(typeof(RootCommand));

        Type[] types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (Type type in types
                     .Where(s => s.IsSubclassOf(typeof(CommandBase)))
                     .Where(s => s.IsClass && !s.IsAbstract)
                     .Where(s => s != typeof(RootCommand)))
        {
            cmdLineHelper.AddType(type);
        }

        if (!cmdLineHelper.TryParse(args, out IList<string>? errors, out Type? commandType, out Dictionary<string, string>? cmdlineValues))
        {
            await Console.Error.WriteLineAsync("Unable to parse commandline, use --help to get details");

            foreach (string error in errors)
                await Console.Error.WriteLineAsync(error);

            return 1;
        }

        if (cmdlineValues.IsHelpRequested())
        {
            cmdLineHelper.PrintHelp(Console.Out, commandType);
            return 2;
        }

        LoggingLevelSwitch logLevel = new();
        LoggingLevelSwitch logLevelPlusOne = new(LogEventLevel.Warning);

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.With<ReduceSourceContextValue>()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContextShort}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.ControlledBy(logLevel)
            .MinimumLevel.Override("MQTTnet", logLevelPlusOne)
            .CreateLogger();

        using IHost host = new HostBuilder()
            .ConfigureAppConfiguration(builder =>
            {
                builder.AddEnvironmentVariables()
                    .AddInMemoryCollection(cmdlineValues);
            })
            .ConfigureLogging((context, builder) =>
            {
                RootCommand.Options? options = context.Configuration.Get<RootCommand.Options>();
                logLevel.MinimumLevel = options.LogLevel;

                // Keep some logs at a persistent higher filter
                logLevelPlusOne.MinimumLevel = (LogEventLevel)Math.Clamp((int)(options.LogLevel + 1),
                    (int)LogEventLevel.Verbose, (int)LogEventLevel.Fatal);

                builder.AddSerilog();
            })
            .ConfigureServices((context, services) =>
            {
                cmdLineHelper.ConfigureServices(services, context.Configuration);

                services.AddSingleton(cmdLineHelper);
                services.AddSingleton(typeof(IValidateOptions<>), typeof(DataAnnotationValidateOptions<>));

                services
                    .AddSingleton<IManagedMqttClientOptions>(_ =>
                    {
                        RootCommand.Options option = _.GetRequiredService<RootCommand.Options>();

                        return new ManagedMqttClientOptionsBuilder()
                            .WithAutoReconnectDelay(TimeSpan.FromSeconds(5))
                            .WithClientOptions(new MqttClientOptionsBuilder()
                                .WithClientId("mqtt_speccer")
                                .WithTcpServer(option.MqttHost, option.MqttPort)
                                .WithCleanSession()
                                .WithProtocolVersion(MqttProtocolVersion.V500)
                                .Build())
                            .Build();
                    })
                    .AddSingleton(provider =>
                    {
                        IHostApplicationLifetime hostLifetime = provider.GetRequiredService<IHostApplicationLifetime>();
                        ILogger<ManagedMqttClient> logger = provider.GetRequiredService<ILogger<ManagedMqttClient>>();
                        MqttLogging mqttLogger = new(logger);

                        IManagedMqttClient mqttClient = new MqttFactory(mqttLogger).CreateManagedMqttClient();

                        hostLifetime.ApplicationStopping.Register(() =>
                        {
                            mqttClient.StopAsync();
                        });
                        mqttClient.UseConnectedHandler(eventArgs =>
                        {
                            logger.LogDebug("MQTT Connected {ResultCode}", eventArgs.ConnectResult.ResultCode);
                        });
                        mqttClient.UseDisconnectedHandler(eventArgs =>
                        {
                            logger.LogWarning("MQTT Disconnected reason: {Reason}", eventArgs.Reason);

                            if (!eventArgs.ClientWasConnected)
                            {
                                // Client was never connected, so stop the app
                                hostLifetime.StopApplication();
                            }
                        });

                        return mqttClient;
                    })
                    .AddSingleton<MqttStore>()
                    .AddSingleton<Z2MApiClient>(provider =>
                    {
                        IHostApplicationLifetime hostLifetime = provider.GetRequiredService<IHostApplicationLifetime>();
                        Z2MApiClient instance = ActivatorUtilities.CreateInstance<Z2MApiClient>(provider, hostLifetime.ApplicationStopping);

                        return instance;
                    });
            })
            .Build();

        CommandBase command;
        try
        {
            command = (CommandBase)host.Services.GetRequiredService(commandType);
        }
        catch (OptionsValidationException e)
        {
            ILogger logger = Log.Logger.ForContext<Program>();
            logger.Error("One or more options were invalid:");

            foreach (string eFailure in e.Failures.Where(s => s != null))
                logger.Error(eFailure);

            logger.Error("Review the help with '--help' for more information");

            return 3;
        }

        try
        {
            await command.ExecuteAsync();
        }
        catch (Exception e)
        {
            ILogger logger = Log.Logger.ForContext<Program>();
            logger.Error(e, "An error occurred");
        }

        return 0;
    }
}