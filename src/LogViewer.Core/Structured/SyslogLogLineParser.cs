using System.Globalization;
using System.Text.RegularExpressions;

namespace LogViewer.Core.Structured;

/// <summary>
/// Parses syslog lines in both the modern <b>RFC 5424</b> shape
/// (<c>&lt;PRI&gt;VERSION TIMESTAMP HOST APP PROCID MSGID [SD] MSG</c>) and the legacy <b>RFC 3164 / BSD</b>
/// shape (<c>&lt;PRI&gt;Mmm dd hh:mm:ss HOST TAG: MSG</c>). The <c>&lt;PRI&gt;</c> value yields both a
/// facility and a severity; the severity maps onto a canonical level. RFC 5424 structured-data elements
/// (<c>[id key="value" ...]</c>) become properties, as do <c>facility</c>, <c>host</c>, <c>appname</c>,
/// <c>procid</c> and <c>msgid</c> when present.
/// </summary>
public sealed partial class SyslogLogLineParser : ILogLineParser
{
    public string FormatId => "syslog";

    public string DisplayName => "Syslog (RFC 5424 / BSD)";

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        evt = null;

        var m = Rfc5424Pattern().Match(line);
        if (m.Success)
        {
            evt = ParseRfc5424(m);
            return true;
        }

        m = Rfc3164Pattern().Match(line);
        if (m.Success)
        {
            evt = ParseRfc3164(m);
            return true;
        }

        return false;
    }

    private static StructuredLogEvent ParseRfc5424(Match m)
    {
        var pri = int.Parse(m.Groups["pri"].Value, CultureInfo.InvariantCulture);
        var severity = pri % 8;
        var facility = pri / 8;

        var properties = new Dictionary<string, string>(StringComparer.Ordinal) { ["facility"] = facility.ToString(CultureInfo.InvariantCulture) };
        AddIfPresent(properties, "host", m.Groups["host"].Value);
        AddIfPresent(properties, "appname", m.Groups["app"].Value);
        AddIfPresent(properties, "procid", m.Groups["procid"].Value);
        AddIfPresent(properties, "msgid", m.Groups["msgid"].Value);

        DateTimeOffset? timestamp = m.Groups["ts"].Value != "-"
            && DateTimeOffset.TryParse(m.Groups["ts"].Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : null;

        var sd = m.Groups["sd"].Value;
        if (sd.Length > 0 && sd != "-")
        {
            foreach (Match pair in SdParamPattern().Matches(sd))
            {
                properties[pair.Groups["k"].Value] = pair.Groups["v"].Value;
            }
        }

        var message = m.Groups["msg"].Value.TrimStart('﻿');

        return new StructuredLogEvent(timestamp, LogLevelNormalizer.FromSyslogSeverity(severity), null, message, null, properties);
    }

    private static StructuredLogEvent ParseRfc3164(Match m)
    {
        var pri = int.Parse(m.Groups["pri"].Value, CultureInfo.InvariantCulture);
        var severity = pri % 8;
        var facility = pri / 8;

        var properties = new Dictionary<string, string>(StringComparer.Ordinal) { ["facility"] = facility.ToString(CultureInfo.InvariantCulture) };
        AddIfPresent(properties, "host", m.Groups["host"].Value);
        AddIfPresent(properties, "tag", m.Groups["tag"].Value);

        DateTimeOffset? timestamp = DateTime.TryParseExact(
            m.Groups["ts"].Value.Replace("  ", " "),
            ["MMM d HH:mm:ss", "MMM dd HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var ts)
            ? new DateTimeOffset(ts)
            : null;

        return new StructuredLogEvent(timestamp, LogLevelNormalizer.FromSyslogSeverity(severity), null, m.Groups["msg"].Value, null, properties);
    }

    private static void AddIfPresent(Dictionary<string, string> properties, string key, string value)
    {
        if (value.Length > 0 && value != "-")
        {
            properties[key] = value;
        }
    }

    [GeneratedRegex(@"^<(?<pri>\d{1,3})>(?<ver>\d{1,2}) (?<ts>\S+) (?<host>\S+) (?<app>\S+) (?<procid>\S+) (?<msgid>\S+) (?<sd>-|(?:\[[^\]]*\])+)(?: (?<msg>.*))?$", RegexOptions.Singleline)]
    private static partial Regex Rfc5424Pattern();

    [GeneratedRegex(@"^<(?<pri>\d{1,3})>(?<ts>[A-Z][a-z]{2}\s+\d{1,2} \d{2}:\d{2}:\d{2}) (?<host>\S+) (?<tag>[^:\[\s]+(?:\[\d+\])?):?\s*(?<msg>.*)$", RegexOptions.Singleline)]
    private static partial Regex Rfc3164Pattern();

    [GeneratedRegex(@"(?<k>[A-Za-z0-9_.-]+)=""(?<v>(?:[^""\\]|\\.)*)""")]
    private static partial Regex SdParamPattern();
}
