namespace WinClonerX.Infrastructure.Windows.Snapshots;

/// <summary>Creates a new VSS session. The real implementation is <see cref="ComVssBackupFactory"/>.</summary>
internal interface IVssBackupFactory
{
    /// <summary>Creates a session. Must be called on the MTA worker thread.</summary>
    IVssSession Create();
}
