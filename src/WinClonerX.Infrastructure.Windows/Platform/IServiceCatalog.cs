namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>Start mode of a Windows service, as configured in the service control manager.</summary>
internal enum ServiceStartKind
{
    Boot,
    System,
    Automatic,
    Manual,
    Disabled,
}

/// <summary>Looks up the configured start mode of Windows services. Read-only.</summary>
internal interface IServiceCatalog
{
    /// <summary>The start mode of <paramref name="serviceName"/>, or null when no such service is installed.</summary>
    ServiceStartKind? GetStartKind(string serviceName);
}
