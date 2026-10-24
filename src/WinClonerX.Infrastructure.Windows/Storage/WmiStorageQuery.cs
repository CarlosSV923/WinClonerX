using System.Globalization;
using System.Management;
using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// WMI implementation of <see cref="IStorageQuery"/>:
/// <c>SELECT AccessPaths FROM MSFT_Partition WHERE Guid = '{...}'</c> in <c>root\Microsoft\Windows\Storage</c>.
/// </summary>
internal sealed class WmiStorageQuery : IStorageQuery
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    public IReadOnlyList<string> GetAccessPaths(Guid partitionGuid)
    {
        // A .NET-formatted GUID holds only hex digits, hyphens and braces, so it cannot inject WQL.
        string guid = partitionGuid.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
        string query = $"SELECT AccessPaths FROM MSFT_Partition WHERE Guid = '{guid}'";

        try
        {
            using var searcher = new ManagementObjectSearcher(StorageScope, query);
            using ManagementObjectCollection results = searcher.Get();

            var paths = new List<string>();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    if (item["AccessPaths"] is string[] values)
                    {
                        paths.AddRange(values.Where(path => !string.IsNullOrEmpty(path)));
                    }
                }
            }

            return paths;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PlatformException(PlatformErrorCode.AccessDenied, "WMI denied the MSFT_Partition query.", ex);
        }
        catch (ManagementException ex)
        {
            throw new PlatformException(PlatformErrorCode.Unknown, $"WMI query failed: {ex.Message}", ex);
        }
    }
}
