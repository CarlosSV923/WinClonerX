namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>
/// One entry printed by <c>bcdedit /enum &lt;id&gt; /v</c>: its identifier, for example <c>{GUID}</c>, and its elements,
/// keyed by their English names.
/// </summary>
internal sealed record BcdEntry(string Identifier, IReadOnlyDictionary<string, string> Elements)
{
    /// <summary>Value of an element, or <c>null</c> when the entry does not have it.</summary>
    internal string? ElementOrNull(string element) => Elements.TryGetValue(element, out string? value) ? value : null;
}

/// <summary>
/// The WIM path and the ramdisk options identifier of a WinRE loader device:
/// <c>ramdisk=[&lt;device&gt;]&lt;WimPath&gt;,{OptionsIdentifier}</c>. The device in brackets is not kept, because the
/// clone's recovery partition replaces it.
/// </summary>
internal sealed record RamdiskDevice(string WimPath, string OptionsIdentifier);

/// <summary>
/// Pure readers for the output of bcdedit (R7). Section headers are localized, so they are never read. The first key line of
/// a block is the identifier, whatever its name, and the element names, which bcdedit prints in English, are matched as they
/// are. Output that cannot be read throws <see cref="FormatException"/>, so the caller fails closed.
/// </summary>
internal static class BcdEnumParser
{
    private const string RamdiskPrefix = "ramdisk=[";

    /// <summary>Reads the output of one <c>/enum</c> run that names a single entry.</summary>
    /// <exception cref="FormatException">The output has no identifier, a repeated element, or a second entry.</exception>
    internal static BcdEntry Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        string[] lines = output.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? identifier = null;
        var elements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (IsSeparator(line))
            {
                continue;
            }

            // A line followed by a dashed line is a section title, in any language.
            if (i + 1 < lines.Length && IsSeparator(lines[i + 1]))
            {
                if (identifier is not null)
                {
                    throw new FormatException("the output holds more than one boot entry");
                }

                continue;
            }

            int gap = line.IndexOfAny([' ', '\t']);
            if (gap <= 0)
            {
                // A further value of a multi-valued element, such as displayorder, has no name of its own.
                continue;
            }

            string name = line[..gap];
            string value = line[gap..].Trim();

            if (identifier is null)
            {
                if (!IsBraced(value))
                {
                    throw new FormatException("the first line of the entry is not an identifier");
                }

                identifier = value;
                continue;
            }

            if (!elements.TryAdd(name, value))
            {
                throw new FormatException($"the element '{name}' is printed twice");
            }
        }

        return identifier is null
            ? throw new FormatException("no identifier was found")
            : new BcdEntry(identifier, elements);
    }

    /// <summary>
    /// Reads a WinRE loader device, for example <c>ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim,{GUID}</c>.
    /// The WIM path may contain spaces. The options identifier is the text after the last comma.
    /// </summary>
    /// <exception cref="FormatException">The value is not a ramdisk device with an absolute path and an options GUID.</exception>
    internal static RamdiskDevice ParseRamdiskDevice(string device)
    {
        ArgumentNullException.ThrowIfNull(device);

        if (!device.StartsWith(RamdiskPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException($"'{device}' is not a ramdisk device");
        }

        int close = device.IndexOf(']', RamdiskPrefix.Length);
        if (close < 0)
        {
            throw new FormatException($"'{device}' has no closing bracket");
        }

        string rest = device[(close + 1)..];
        int comma = rest.LastIndexOf(',');
        if (comma < 0)
        {
            throw new FormatException($"'{device}' has no options identifier");
        }

        string wimPath = rest[..comma];
        string options = rest[(comma + 1)..];

        if (wimPath.Length < 2 || !wimPath.StartsWith('\\') || !IsGuid(options))
        {
            throw new FormatException($"'{device}' does not have an absolute WIM path and a GUID options identifier");
        }

        return new RamdiskDevice(wimPath, options);
    }

    /// <summary>True for an identifier printed as a GUID in braces, which is how <c>bcdedit /v</c> prints entries.</summary>
    internal static bool IsGuid(string value) =>
        value.Length == 38 && value[0] == '{' && value[37] == '}' && Guid.TryParseExact(value.AsSpan(1, 36), "D", out _);

    private static bool IsBraced(string value) => value.Length >= 3 && value[0] == '{' && value[^1] == '}';

    private static bool IsSeparator(string line) => line.Length >= 3 && line.All(static c => c == '-');
}
