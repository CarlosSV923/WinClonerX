using System.Globalization;
using System.Management;
using System.Text.RegularExpressions;
using WinCopy.Application.Errors;

namespace WinCopy.Infrastructure.Windows.Platform;

/// <summary>
/// Implementation of <see cref="IServiceCatalog"/> with <c>Win32_Service.StartMode</c> in <c>root\cimv2</c>.
/// It is used instead of <c>System.ServiceProcess.ServiceController</c>, which is not in the net10.0-windows
/// framework and would need a new package. Read-only.
/// </summary>
internal sealed partial class WmiServiceCatalog : IServiceCatalog
{
    private const string CimScope = @"root\cimv2";

    public ServiceStartKind? GetStartKind(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // Service names are letters, digits, '_', '-' and '.'. Anything else is refused before it goes into WQL.
        if (!ServiceNamePattern().IsMatch(serviceName))
        {
            throw new ArgumentException("Not a valid service name.", nameof(serviceName));
        }

        string startMode = ReadStartMode(serviceName);
        if (startMode.Length == 0)
        {
            return null;
        }

        return startMode switch
        {
            "Boot" => ServiceStartKind.Boot,
            "System" => ServiceStartKind.System,
            "Auto" => ServiceStartKind.Automatic,
            "Manual" => ServiceStartKind.Manual,
            "Disabled" => ServiceStartKind.Disabled,
            _ => throw new InvalidOperationException($"Unknown start mode '{startMode}' for service '{serviceName}'."),
        };
    }

    /// <summary>The StartMode text of the service, or an empty string when the service is not installed.</summary>
    private static string ReadStartMode(string serviceName)
    {
        string wql = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT StartMode FROM Win32_Service WHERE Name = '{serviceName}'");

        try
        {
            using var searcher = new ManagementObjectSearcher(CimScope, wql);
            using ManagementObjectCollection results = searcher.Get();

            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    return Convert.ToString(item["StartMode"], CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PlatformException(PlatformErrorCode.AccessDenied, "WMI denied the service query.", ex);
        }
        catch (ManagementException ex)
        {
            throw new PlatformException(PlatformErrorCode.Unknown, $"WMI service query failed: {ex.Message}", ex);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ServiceNamePattern();
}
