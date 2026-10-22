using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinClonerX.Infrastructure.Windows.Interop.Vss;

/// <summary>
/// IVssBackupComponents from vss.h, IID 665c1d5f-c218-414d-a05d-7fef5f9d5c86.
/// </summary>
/// <remarks>
/// THE SLOTS MUST NOT BE REORDERED. Every call goes to a slot by its index, so the order below is the vtable order.
/// Slot N is the N-th method declared here, counted from 1, after IUnknown. Do not add, remove or move methods.
/// Methods WinClonerX never calls keep their slot and their name but use a parameterless placeholder signature.
/// Never call a placeholder. The signatures of the methods that WinClonerX calls are the real ones.
/// The VssInteropLayoutTests test class checks the count, the order and the signatures of the called methods.
/// </remarks>
[GeneratedComInterface]
[Guid("665c1d5f-c218-414d-a05d-7fef5f9d5c86")]
internal partial interface IVssBackupComponents
{
    // Slot 1.
    [PreserveSig]
    int GetWriterComponentsCount();

    // Slot 2.
    [PreserveSig]
    int GetWriterComponents();

    // Slot 3. bstrXml is a BSTR; 0 means no XML.
    [PreserveSig]
    int InitializeForBackup(nint bstrXml);

    // Slot 4. The three leading parameters are C++ bool, which is 1 byte, so U1 is used.
    [PreserveSig]
    int SetBackupState(
        [MarshalAs(UnmanagedType.U1)] bool selectComponents,
        [MarshalAs(UnmanagedType.U1)] bool backupBootableSystemState,
        int backupType,
        [MarshalAs(UnmanagedType.U1)] bool partialFileSupport);

    // Slot 5.
    [PreserveSig]
    int InitializeForRestore();

    // Slot 6.
    [PreserveSig]
    int SetRestoreState();

    // Slot 7.
    [PreserveSig]
    int GatherWriterMetadata(out nint ppAsync);

    // Slot 8.
    [PreserveSig]
    int GetWriterMetadataCount();

    // Slot 9.
    [PreserveSig]
    int GetWriterMetadata();

    // Slot 10.
    [PreserveSig]
    int FreeWriterMetadata();

    // Slot 11.
    [PreserveSig]
    int AddComponent();

    // Slot 12.
    [PreserveSig]
    int PrepareForBackup(out nint ppAsync);

    // Slot 13.
    [PreserveSig]
    int AbortBackup();

    // Slot 14.
    [PreserveSig]
    int GatherWriterStatus();

    // Slot 15.
    [PreserveSig]
    int GetWriterStatusCount();

    // Slot 16.
    [PreserveSig]
    int FreeWriterStatus();

    // Slot 17.
    [PreserveSig]
    int GetWriterStatus();

    // Slot 18.
    [PreserveSig]
    int SetBackupSucceeded();

    // Slot 19.
    [PreserveSig]
    int SetBackupOptions();

    // Slot 20.
    [PreserveSig]
    int SetSelectedForRestore();

    // Slot 21.
    [PreserveSig]
    int SetRestoreOptions();

    // Slot 22.
    [PreserveSig]
    int SetAdditionalRestores();

    // Slot 23.
    [PreserveSig]
    int SetPreviousBackupStamp();

    // Slot 24.
    [PreserveSig]
    int SaveAsXML();

    // Slot 25.
    [PreserveSig]
    int BackupComplete(out nint ppAsync);

    // Slot 26.
    [PreserveSig]
    int AddAlternativeLocationMapping();

    // Slot 27.
    [PreserveSig]
    int AddRestoreSubcomponent();

    // Slot 28.
    [PreserveSig]
    int SetFileRestoreStatus();

    // Slot 29.
    [PreserveSig]
    int AddNewTarget();

    // Slot 30.
    [PreserveSig]
    int SetRangesFilePath();

    // Slot 31.
    [PreserveSig]
    int PreRestore();

    // Slot 32.
    [PreserveSig]
    int PostRestore();

    // Slot 33.
    [PreserveSig]
    int SetContext(int lContext);

    // Slot 34.
    [PreserveSig]
    int StartSnapshotSet(out Guid pSnapshotSetId);

    // Slot 35. providerId = GUID_NULL selects the default system provider.
    [PreserveSig]
    int AddToSnapshotSet(
        [MarshalAs(UnmanagedType.LPWStr)] string pwszVolumeName,
        Guid providerId,
        out Guid pidSnapshot);

    // Slot 36.
    [PreserveSig]
    int DoSnapshotSet(out nint ppAsync);

    // Slot 37. bForceDelete is a Win32 BOOL (4 bytes), so Bool is used and not U1.
    [PreserveSig]
    int DeleteSnapshots(
        Guid sourceObjectId,
        int eSourceObjectType,
        [MarshalAs(UnmanagedType.Bool)] bool bForceDelete,
        out int plDeletedSnapshots,
        out Guid pNondeletedSnapshotId);

    // Slot 38.
    [PreserveSig]
    int ImportSnapshots();

    // Slot 39.
    [PreserveSig]
    int BreakSnapshotSet();

    // Slot 40. pProp is filled by VSS. Free it with VssApi.VssFreeSnapshotProperties.
    [PreserveSig]
    int GetSnapshotProperties(Guid snapshotId, out VSS_SNAPSHOT_PROP pProp);

    // Slot 41.
    [PreserveSig]
    int Query();

    // Slot 42. pbSupportedByThisProvider is a Win32 BOOL (4 bytes), so Bool is used and not U1.
    [PreserveSig]
    int IsVolumeSupported(
        Guid providerId,
        [MarshalAs(UnmanagedType.LPWStr)] string pwszVolumeName,
        [MarshalAs(UnmanagedType.Bool)] out bool pbSupportedByThisProvider);

    // Slot 43.
    [PreserveSig]
    int DisableWriterClasses();

    // Slot 44.
    [PreserveSig]
    int EnableWriterClasses();

    // Slot 45.
    [PreserveSig]
    int DisableWriterInstances();

    // Slot 46.
    [PreserveSig]
    int ExposeSnapshot();

    // Slot 47.
    [PreserveSig]
    int RevertToSnapshot();

    // Slot 48.
    [PreserveSig]
    int QueryRevertStatus();
}
