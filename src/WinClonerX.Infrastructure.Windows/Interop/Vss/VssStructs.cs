using System.Runtime.InteropServices;

namespace WinClonerX.Infrastructure.Windows.Interop.Vss;

/// <summary>
/// VSS_SNAPSHOT_PROP from vss.h. Blittable and sequential. The <c>nint</c> members are native string pointers owned
/// by VSS. Read them, then release the whole structure with <see cref="VssApi.VssFreeSnapshotProperties"/>.
/// </summary>
/// <remarks>
/// On x64 the natural layout gives a size of 128 bytes, with <c>SnapshotDeviceObject</c> at offset 40 and
/// <c>CreationTimestamp</c> at offset 112. The layout test asserts these values.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct VSS_SNAPSHOT_PROP
{
    public Guid SnapshotId;
    public Guid SnapshotSetId;
    public int SnapshotsCount;
    public nint SnapshotDeviceObject;
    public nint OriginalVolumeName;
    public nint OriginatingMachine;
    public nint ServiceMachine;
    public nint ExposedName;
    public nint ExposedPath;
    public Guid ProviderId;
    public int SnapshotAttributes;
    public long CreationTimestamp;
    public int Status;
}
