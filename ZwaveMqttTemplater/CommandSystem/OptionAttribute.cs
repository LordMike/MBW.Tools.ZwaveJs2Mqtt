namespace ZwaveMqttTemplater.CommandSystem;

[AttributeUsage(AttributeTargets.Property)]
internal class OptionAttribute : Attribute
{
    public string[] Template { get; }
    public string Description { get; }

    public OptionAttribute(string template, string description = null)
    {
        Template = template.Split('|');
        Description = description;
    }
}