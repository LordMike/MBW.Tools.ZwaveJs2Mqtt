#nullable enable
using Serilog.Core;
using Serilog.Events;

namespace ZwaveMqttTemplater.Helpers;

internal class ReduceSourceContextValue : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (!logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out LogEventPropertyValue? sourceContext) ||
            sourceContext is not ScalarValue asScalar ||
            asScalar.Value is not string asString ||
            string.IsNullOrEmpty(asString))
            return;

        // Find last '<'
        int idx = asString.IndexOf('<');
        if (idx > 0)
        {
            // Find last '.' before the '<'
            int dotIdx = asString.LastIndexOf('.', idx);

            if (dotIdx > 0)
            {
                // Substring
                asString = asString.Substring(dotIdx + 1, idx - dotIdx - 1);
            }
        }
        else
        {
            // Find last '.'
            int dotIdx = asString.LastIndexOf('.');

            if (dotIdx > 0)
            {
                // Substring
                asString = asString.Substring(dotIdx + 1);
            }
        }

        ScalarValue value = new(asString);
        logEvent.AddOrUpdateProperty(new LogEventProperty("SourceContextShort", value));
    }
}