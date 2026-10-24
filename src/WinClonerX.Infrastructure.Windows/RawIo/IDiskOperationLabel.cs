namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>
/// Name that a source or a target gives itself in the <c>DiskOperation</c> events of the log. The copier reads it when it
/// records a copy, so the Read names the source and the Write names the target. Infrastructure only: the Application ports
/// do not carry it.
/// </summary>
internal interface IDiskOperationLabel
{
    /// <summary>Value of the <c>Disk</c> property of the events of this object.</summary>
    string DiskOperationLabel { get; }
}
