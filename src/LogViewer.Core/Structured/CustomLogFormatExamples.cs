namespace LogViewer.Core.Structured;

/// <summary>Ready-made starting points offered by the custom-format editor for common text layouts the built-in
/// parsers don't cover. Each is a normal <see cref="CustomLogFormat"/> the user can then tweak.</summary>
public static class CustomLogFormatExamples
{
    public static IReadOnlyList<CustomLogFormat> All { get; } =
    [
        new()
        {
            Id = CustomLogFormat.IdPrefix + "example-log4j",
            Name = "log4j / logback / NLog",
            Pattern = @"^(?<timestamp>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d+)?)\s+\[(?<thread>[^\]]+)\]\s+(?<level>[A-Za-z]+)\s+(?<logger>\S+)\s+-\s+(?<message>.*)$",
        },
        new()
        {
            Id = CustomLogFormat.IdPrefix + "example-python",
            Name = "Python logging",
            Pattern = @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3}) - (?<logger>\S+) - (?<level>[A-Z]+) - (?<message>.*)$",
        },
        new()
        {
            Id = CustomLogFormat.IdPrefix + "example-access",
            Name = "nginx / Apache access log",
            Pattern = @"^(?<client>\S+) \S+ (?<user>\S+) \[(?<timestamp>[^\]]+)\] ""(?<method>[A-Z]+) (?<path>\S+)[^""]*"" (?<status>\d{3}) (?<bytes>\d+|-)(?: ""(?<referer>[^""]*)"" ""(?<agent>[^""]*)"")?",
            TimestampFormat = "dd/MMM/yyyy:HH:mm:ss zzz",
        },
        new()
        {
            Id = CustomLogFormat.IdPrefix + "example-generic",
            Name = "Timestamp + level + message",
            Pattern = @"^(?<timestamp>\d{4}-\d{2}-\d{2}[ T][\d:.,]+(?:Z|[+-]\d{2}:?\d{2})?)\s+\[?(?<level>TRACE|DEBUG|INFO|NOTICE|WARN(?:ING)?|ERROR|FATAL|CRIT(?:ICAL)?)\]?\s+(?<message>.*)$",
            IgnoreCase = true,
        },
    ];
}
