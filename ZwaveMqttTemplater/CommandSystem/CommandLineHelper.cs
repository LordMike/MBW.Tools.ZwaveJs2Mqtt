#nullable enable
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ZwaveMqttTemplater.CommandSystem;

internal class CommandLineHelper<TBaseType>
{
    public RootCommand RootCommand { get; set; }

    private readonly List<CommandDetails> _commandMap = new();

    public CommandLineHelper(Type rootType)
    {
        EnsureBaseType(rootType);
        RootCommand = AddTypeInternal(new RootCommand(), rootType);
    }

    private static void EnsureBaseType(Type type)
    {
        if (!type.IsSubclassOf(typeof(TBaseType)))
            throw new Exception("Type " + type + " must inherit from " + typeof(TBaseType));
    }

    public void AddType(Type commandType)
    {
        CommandAttribute? commandAttribute = commandType.GetCustomAttribute<CommandAttribute>();
        if (commandAttribute == null)
            throw new ArgumentException($"Type {commandType} must have {nameof(CommandAttribute)} attribute");

        Command cmd = AddTypeInternal(new Command(commandAttribute.Name, commandAttribute.Description), commandType);

        RootCommand.AddCommand(cmd);
    }

    private TCommand AddTypeInternal<TCommand>(TCommand command, Type commandType) where TCommand : Command
    {
        EnsureBaseType(commandType);

        command.SetHandler(() => { });

        CommandDetails details = new()
        {
            Command = command,
            CommandType = commandType
        };

        CommandAttribute? commandAttribute = commandType.GetCustomAttribute<CommandAttribute>();
        if (commandAttribute?.OptionsType != null)
        {
            details.OptionsType = commandAttribute.OptionsType;

            foreach (PropertyInfo property in commandAttribute.OptionsType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                bool required = property.GetCustomAttribute<RequiredAttribute>() != null;

                ArgumentArity arity = required ? ArgumentArity.ExactlyOne : ArgumentArity.ZeroOrOne;
                if (property.PropertyType.IsArray)
                    arity = required ? ArgumentArity.OneOrMore : ArgumentArity.ZeroOrMore;

                OptionAttribute? optionAttribute = property.GetCustomAttribute<OptionAttribute>();
                if (optionAttribute != null)
                {
                    Option option = new(optionAttribute.Template, optionAttribute.Description, property.PropertyType, arity: arity);
                    command.AddOption(option);

                    // Ensure we use specified template to map to property
                    details.Options.Add((option, property));
                }

                ArgumentAttribute? argumentAttribute = property.GetCustomAttribute<ArgumentAttribute>();
                if (argumentAttribute != null)
                {
                    Argument argument = new(argumentAttribute.Name, argumentAttribute.Description);
                    argument.Arity = arity;
                    command.AddArgument(argument);

                    details.Arguments.Add((argument, property));
                }
            }
        }

        _commandMap.Add(details);

        return command;
    }

    public bool TryParse(string[] args, [NotNullWhen(false)] out IList<string>? errors, [NotNullWhen(true)] out Type? selectedCommandType, [NotNullWhen(true)] out Dictionary<string, string>? values)
    {
        Parser parser = new CommandLineBuilder(RootCommand)
            .UseParseErrorReporting()
            .Build();

        ParseResult res = parser.Parse(args);
        if (res.Errors.Any())
        {
            errors = res.Errors.Select(s => s.Message).ToArray();
            values = default;
            selectedCommandType = default;
            return false;
        }

        Dictionary<string, string> valueRes = new(StringComparer.OrdinalIgnoreCase);

        void Apply(CommandDetails details)
        {
            foreach ((Option option, PropertyInfo property) option in details.Options)
            {
                if (!res.HasOption(option.option))
                    continue;

                object? value = res.GetValueForOption(option.option);
                valueRes.Add(option.property.Name, value!.ToString()!);
            }

            foreach ((Argument argument, PropertyInfo property) option in details.Arguments)
            {
                object? value = res.GetValueForArgument(option.argument);

                if (value != null)
                    valueRes.Add(option.property.Name, value!.ToString()!);
            }
        }

        {
            // Add in actual command
            CommandDetails selectedCmd = _commandMap.Single(x => x.Command == res.CommandResult.Command);
            Apply(selectedCmd);

            selectedCommandType = selectedCmd.CommandType;
        }

        if (res.RootCommandResult.Command != res.CommandResult.Command)
        {
            // Add in root command
            CommandDetails selectedCmd = _commandMap.Single(x => x.Command == res.RootCommandResult.Command);
            Apply(selectedCmd);
        }

        errors = default;
        values = valueRes;

        return true;
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        foreach (CommandDetails details in _commandMap)
        {
            if (details.OptionsType != null)
            {
                // Register options type
                MethodInfo method = GetType().GetMethod(nameof(RegisterOptionsType), BindingFlags.Static | BindingFlags.NonPublic)!;
                method = method.MakeGenericMethod(details.OptionsType);

                method.Invoke(this, new object[] { services, configuration });
            }

            // Register command itself
            services.AddSingleton(details.CommandType);
        }
    }

    private static void RegisterOptionsType<TOptions>(IServiceCollection services, IConfiguration configuration) where TOptions : class
    {
        services.Configure<TOptions>(configuration);
        services.AddSingleton(x => x.GetRequiredService<IOptions<TOptions>>().Value);
    }

    public void PrintHelp(TextWriter output, Type command)
    {
        CommandDetails details = _commandMap.First(s => s.CommandType == command);

        new HelpBuilder(LocalizationResources.Instance, 80).Write(details.Command, output);
    }

    private class CommandDetails
    {
        public Command Command;
        public Type CommandType { get; set; }
        public Type? OptionsType { get; set; }

        public List<(Option option, PropertyInfo property)> Options { get; } = new();
        public List<(Argument argument, PropertyInfo property)> Arguments { get; } = new();
    }
}