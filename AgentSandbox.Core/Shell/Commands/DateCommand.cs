using System.Globalization;
using System.Text;

namespace AgentSandbox.Core.Shell.Commands;

/// <summary>
/// Displays the current date and time.
/// Supports common format specifiers via +FORMAT syntax.
/// </summary>
public class DateCommand : IShellCommand
{
    public string Name => "date";
    public string Description => "Display the current date and time";
    public string Usage => "date [+FORMAT]\n\nFormat specifiers:\n  %Y - Year (2026)\n  %m - Month (01-12)\n  %d - Day (01-31)\n  %H - Hour (00-23)\n  %M - Minute (00-59)\n  %S - Second (00-59)\n  %s - Unix timestamp\n  %F - Full date (%Y-%m-%d)\n  %T - Time (%H:%M:%S)\n  %Z - Timezone\n\nExamples:\n  date\n  date +%Y-%m-%d\n  date +\"%Y-%m-%d %H:%M:%S\"";

    public ShellResult Execute(string[] args, IShellContext context)
    {
        var now = DateTime.UtcNow;

        if (args.Length == 0)
        {
            // Default format: "Tue Feb  4 17:30:23 UTC 2026"
            var result = now.ToString("ddd MMM ", CultureInfo.InvariantCulture) +
                         now.Day.ToString().PadLeft(2) + " " +
                         now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
                         " UTC " +
                         now.ToString("yyyy", CultureInfo.InvariantCulture);
            return ShellResult.Ok(result);
        }

        var format = args[0];
        
        // Handle +FORMAT syntax
        if (format.StartsWith('+'))
        {
            format = format[1..];
            // Remove surrounding quotes if present
            if ((format.StartsWith('"') && format.EndsWith('"')) ||
                (format.StartsWith('\'') && format.EndsWith('\'')))
            {
                format = format[1..^1];
            }
            
            var output = ConvertFormat(format, now);
            return ShellResult.Ok(output);
        }

        return ShellResult.Error($"date: invalid option -- '{format}'\nUsage: {Usage}");
    }

    private static string ConvertFormat(string format, DateTime now)
    {
        var sb = new StringBuilder();
        
        for (int i = 0; i < format.Length; i++)
        {
            if (format[i] == '%' && i + 1 < format.Length)
            {
                var specifier = format[i + 1];
                sb.Append(GetFormatValue(specifier, now));
                i++; // Skip the specifier
            }
            else
            {
                sb.Append(format[i]);
            }
        }

        return sb.ToString();
    }

    private static string GetFormatValue(char specifier, DateTime now)
    {
        return specifier switch
        {
            'Y' => now.ToString("yyyy", CultureInfo.InvariantCulture),
            'y' => now.ToString("yy", CultureInfo.InvariantCulture),
            'm' => now.ToString("MM", CultureInfo.InvariantCulture),
            'd' => now.ToString("dd", CultureInfo.InvariantCulture),
            'e' => now.Day.ToString().PadLeft(2),
            'H' => now.ToString("HH", CultureInfo.InvariantCulture),
            'I' => now.ToString("hh", CultureInfo.InvariantCulture),
            'M' => now.ToString("mm", CultureInfo.InvariantCulture),
            'S' => now.ToString("ss", CultureInfo.InvariantCulture),
            's' => ((DateTimeOffset)now).ToUnixTimeSeconds().ToString(),
            'N' => (now.Ticks % TimeSpan.TicksPerSecond * 100).ToString("D9"),
            'j' => now.DayOfYear.ToString("D3"),
            'u' => ((int)now.DayOfWeek == 0 ? 7 : (int)now.DayOfWeek).ToString(),
            'w' => ((int)now.DayOfWeek).ToString(),
            'U' => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstDay, DayOfWeek.Sunday).ToString("D2"),
            'W' => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(now, CalendarWeekRule.FirstDay, DayOfWeek.Monday).ToString("D2"),
            'a' => now.ToString("ddd", CultureInfo.InvariantCulture),
            'A' => now.ToString("dddd", CultureInfo.InvariantCulture),
            'b' => now.ToString("MMM", CultureInfo.InvariantCulture),
            'B' => now.ToString("MMMM", CultureInfo.InvariantCulture),
            'p' => now.ToString("tt", CultureInfo.InvariantCulture).ToUpperInvariant(),
            'P' => now.ToString("tt", CultureInfo.InvariantCulture).ToLowerInvariant(),
            'Z' => "UTC",
            'z' => "+0000",
            'F' => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            'T' => now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            'R' => now.ToString("HH:mm", CultureInfo.InvariantCulture),
            'D' => now.ToString("MM/dd/yy", CultureInfo.InvariantCulture),
            'n' => "\n",
            't' => "\t",
            '%' => "%",
            _ => $"%{specifier}" // Unknown specifier, keep as-is
        };
    }
}
