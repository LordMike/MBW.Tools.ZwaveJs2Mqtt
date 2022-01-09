using Microsoft.Extensions.Logging;
using ZwaveMqttTemplater.Mqtt;
using ZwaveMqttTemplater.Z2M;
using ZwaveMqttTemplater.Z2M.Models;

namespace ZwaveMqttTemplater.Helpers;

internal static class CommandHelpers
{
    public static async Task<List<Z2MNode>> GetNodesByFilter(Z2MApiClient client, string filter)
    {
        Z2MNodes nodes = await client.GetNodes();
        return GetNodesByFilter(nodes, filter).ToList();
    }

    public static IEnumerable<Z2MNode> GetNodesByFilter(Z2MNodes nodes, string filter)
    {
        return nodes.FilterByString(filter);
    }

    public static async Task<FlushResult> TopicPromptAndFlush(ILogger logger, MqttStore store, bool autoConfirm, bool verbose, bool delayBetweenPublish = false)
    {
        List<string> topics = store.GetTopicsToSet().ToList();
        if (!topics.Any())
            return FlushResult.NoTopicsToFlush;

        Console.WriteLine("Will set the following topics:");
        foreach (string topic in topics)
        {
            Console.WriteLine($"> {topic}");

            if (verbose)
            {
                store.TryGetString(topic, out string previous, true);
                store.TryGetString(topic, out string desired);

                Console.WriteLine("Before: " + previous);
                Console.WriteLine("After:  " + desired);
                Console.WriteLine();
            }
        }

        bool doChanges = autoConfirm || GetYesNo("Process these topics?", true, ConsoleColor.DarkRed);
        if (!doChanges)
        {
            logger.LogWarning("No changes were made");
            return FlushResult.UserAborted;
        }

        await store.FlushTopicsToSet(delayBetweenPublish);
        return FlushResult.FlushedTopics;
    }

    private static bool GetYesNo(string prompt, bool defaultAnswer, ConsoleColor? promptColor = null, ConsoleColor? promptBgColor = null)
    {
        var answerHint = defaultAnswer ? "[Y/n]" : "[y/N]";
        do
        {
            Write($"{prompt} {answerHint}", promptColor, promptBgColor);
            Console.Write(' ');

            string? resp;
            using (ShowCursor())
            {
                resp = Console.ReadLine()?.ToLower()?.Trim();
            }

            if (string.IsNullOrEmpty(resp))
            {
                return defaultAnswer;
            }

            switch (resp)
            {
                case "n":
                case "no":
                    return false;
                case "y":
                case "yes":
                    return true;
                default:
                    Console.WriteLine($"Invalid response '{resp}'. Please answer 'y' or 'n' or CTRL+C to exit.");
                    break;
            }
        }
        while (true);
    }

    private static void Write(string value, ConsoleColor? foreground, ConsoleColor? background)
    {
        if (foreground.HasValue)
        {
            Console.ForegroundColor = foreground.Value;
        }

        if (background.HasValue)
        {
            Console.BackgroundColor = background.Value;
        }

        Console.Write(value);

        if (foreground.HasValue || background.HasValue)
        {
            Console.ResetColor();
        }
    }

    private static IDisposable ShowCursor() => new CursorState();

    private class CursorState : IDisposable
    {
        private readonly bool _original;

        public CursorState()
        {
            try
            {
                _original = Console.CursorVisible;
            }
            catch
            {
                // some platforms throw System.PlatformNotSupportedException
                // Assume the cursor should be shown
                _original = true;
            }

            TrySetVisible(true);
        }

        private void TrySetVisible(bool visible)
        {
            try
            {
                Console.CursorVisible = visible;
            }
            catch
            {
                // setting cursor may fail if output is piped or permission is denied.
            }
        }

        public void Dispose()
        {
            TrySetVisible(_original);
        }
    }

    public enum FlushResult
    {
        Unknown,
        FlushedTopics,
        UserAborted,
        NoTopicsToFlush
    }
}