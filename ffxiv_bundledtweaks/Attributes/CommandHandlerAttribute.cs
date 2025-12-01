namespace ComplexTweaks.Attributes;

[AttributeUsage(AttributeTargets.Method)]
public class CommandHandlerAttribute(string[] commands, string helpMessage, string? configFieldName = null, params string[] subCommandStrings) : Attribute {
    public string[] Commands { get; } = commands;
    public string HelpMessage { get; } = helpMessage;
    public string? ConfigFieldName { get; } = configFieldName;
    public List<SubCommand> SubCommands { get; } = [.. subCommandStrings
        .Select(s => s.Split('|', 2))
        .Select(parts => new SubCommand(parts[0], parts[1]))];

    public CommandHandlerAttribute(string command, string helpMessage, string? configFieldName = null, params string[] subCommandStrings) : this([command], helpMessage, configFieldName, subCommandStrings) { }

    public static string UnitSubCommand(string unit, string description)
        => $"[0-9]{unit}|{description}";
}

// microsoft please let me be able to pass this to the attribute
public class SubCommand(string subcommand, string helpMessage) {
    public string Subcommand { get; } = subcommand;
    public string HelpMessage { get; } = helpMessage;

    public static SubCommand FromAttribute(CommandHandlerAttribute attr, int index)
        => new(attr.SubCommands[index].Subcommand, attr.SubCommands[index].HelpMessage);
}
