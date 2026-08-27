using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AvaloniaAppMPV.Infrastructure.Mpv;

public static class MpvConfigValidator
{
    private static readonly HashSet<string> ForbiddenOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "vo", "gpu-api", "gpu-context", "wid", "window-id", "force-window",
    };

    public static IReadOnlyList<string> FindForbiddenOptions(string path)
    {
        var result = new List<string>();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var commentIndex = line.IndexOf('#');
            if (commentIndex >= 0)
                line = line[..commentIndex].Trim();

            var separator = line.IndexOf('=');
            var whitespace = line.IndexOfAny([' ', '\t']);
            if (separator < 0 || (whitespace >= 0 && whitespace < separator))
                separator = whitespace;

            var option = (separator >= 0 ? line[..separator] : line).Trim();
            if (ForbiddenOptions.Contains(option) && !result.Any(x => string.Equals(x, option, StringComparison.OrdinalIgnoreCase)))
                result.Add(option);
        }

        return result;
    }
}
