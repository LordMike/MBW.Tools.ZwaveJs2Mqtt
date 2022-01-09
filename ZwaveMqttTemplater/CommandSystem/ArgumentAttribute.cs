namespace ZwaveMqttTemplater.CommandSystem;

[AttributeUsage(AttributeTargets.Property)]
internal class ArgumentAttribute : Attribute
{
    public string Name { get; }
    public string Description { get; }

    public ArgumentAttribute(string name, string description = null)
    {
        Name = name;
        Description = description;
    }
}