using Microsoft.Extensions.Logging;
using MQTTnet.Diagnostics.Logger;

namespace ZwaveMqttTemplater.Mqtt;

internal class MqttLogging : IMqttNetLogger
{
    private readonly ILogger _logger;

    public MqttLogging(ILogger logger)
    {
        _logger = logger;
    }

    public void Publish(MqttNetLogLevel logLevel, string source, string message, object[] parameters, Exception exception)
    {
        LogLevel level;
        switch (logLevel)
        {
            case MqttNetLogLevel.Verbose:
                level = LogLevel.Debug;
                break;
            case MqttNetLogLevel.Info:
                level = LogLevel.Information;
                break;
            case MqttNetLogLevel.Warning:
                level = LogLevel.Warning;
                break;
            case MqttNetLogLevel.Error:
                level = LogLevel.Error;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null);
        }

        _logger.Log(level, exception, message, parameters);
    }
}