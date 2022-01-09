namespace ZwaveMqttTemplater.CommandSystem;

[AttributeUsage(AttributeTargets.Class)]
internal class CommandAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }
    public Type OptionsType { get; }

    public CommandAttribute(string name, string description = null, Type optionsType = null)
    {
        Name = name;
        Description = description;
        OptionsType = optionsType;
    }
}