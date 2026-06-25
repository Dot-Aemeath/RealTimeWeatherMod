using System;
using System.Globalization;

namespace ChillWithYou.EnvSync.Core
{
    internal static class SceneryAutomationTriggerStateCodec
    {
        internal struct Entry
        {
            public bool Triggered;
            public bool RollDone;
            public DateTime StartTime;
        }

        internal static string FormatEntry(bool triggered, bool rollDone)
        {
            return FormatEntry(triggered, rollDone, DateTime.MinValue);
        }

        internal static string FormatEntry(bool triggered, bool rollDone, DateTime startTime)
        {
            string flags = string.Empty;
            if (triggered) flags += "T";
            if (rollDone) flags += "R";

            if (flags.Length == 0)
                return null;

            if (startTime != DateTime.MinValue)
                return flags + ";" + startTime.ToString("o", CultureInfo.InvariantCulture);

            return flags;
        }

        internal static bool TryParseEntry(string value, out Entry entry)
        {
            entry = new Entry { StartTime = DateTime.MinValue };
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string[] parts = value.Split(';');
            string flags = parts[0].Trim();

            entry.Triggered = flags.Contains("T");
            entry.RollDone = flags.Contains("R");

            if (parts.Length > 1)
            {
                DateTime startTime;
                if (DateTime.TryParseExact(
                    parts[1].Trim(),
                    "o",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out startTime))
                {
                    entry.StartTime = startTime;
                }
            }

            return entry.Triggered || entry.RollDone || entry.StartTime != DateTime.MinValue;
        }

        internal static bool TryParseDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }
    }
}
