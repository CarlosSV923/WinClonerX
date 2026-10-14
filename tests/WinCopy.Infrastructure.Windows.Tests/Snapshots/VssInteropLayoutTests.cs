using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Shouldly;
using WinCopy.Infrastructure.Windows.Interop.Vss;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Snapshots;

// Layout and contract checks only. These tests never create a COM object and never call VSS.
public sealed class VssInteropLayoutTests
{
    // The 48 methods of IVssBackupComponents after IUnknown, in vtable order (vss.h).
    private static readonly string[] ExpectedBackupComponentsMethods =
    [
        "GetWriterComponentsCount",
        "GetWriterComponents",
        "InitializeForBackup",
        "SetBackupState",
        "InitializeForRestore",
        "SetRestoreState",
        "GatherWriterMetadata",
        "GetWriterMetadataCount",
        "GetWriterMetadata",
        "FreeWriterMetadata",
        "AddComponent",
        "PrepareForBackup",
        "AbortBackup",
        "GatherWriterStatus",
        "GetWriterStatusCount",
        "FreeWriterStatus",
        "GetWriterStatus",
        "SetBackupSucceeded",
        "SetBackupOptions",
        "SetSelectedForRestore",
        "SetRestoreOptions",
        "SetAdditionalRestores",
        "SetPreviousBackupStamp",
        "SaveAsXML",
        "BackupComplete",
        "AddAlternativeLocationMapping",
        "AddRestoreSubcomponent",
        "SetFileRestoreStatus",
        "AddNewTarget",
        "SetRangesFilePath",
        "PreRestore",
        "PostRestore",
        "SetContext",
        "StartSnapshotSet",
        "AddToSnapshotSet",
        "DoSnapshotSet",
        "DeleteSnapshots",
        "ImportSnapshots",
        "BreakSnapshotSet",
        "GetSnapshotProperties",
        "Query",
        "IsVolumeSupported",
        "DisableWriterClasses",
        "EnableWriterClasses",
        "DisableWriterInstances",
        "ExposeSnapshot",
        "RevertToSnapshot",
        "QueryRevertStatus",
    ];

    private static readonly string[] ExpectedAsyncMethods = ["Cancel", "Wait", "QueryStatus"];

    // ---- IVssBackupComponents: identity, count and order ----

    [Fact]
    public void BackupComponentsIidMatchesVssHeader() =>
        typeof(IVssBackupComponents).GUID.ShouldBe(Guid.Parse("665c1d5f-c218-414d-a05d-7fef5f9d5c86"));

    [Fact]
    public void BackupComponentsIsGeneratedComInterface() =>
        typeof(IVssBackupComponents).GetCustomAttribute<GeneratedComInterfaceAttribute>().ShouldNotBeNull();

    [Fact]
    public void BackupComponentsHasExactly48MethodsInVtableOrder()
    {
        MethodInfo[] methods = typeof(IVssBackupComponents).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        methods.Length.ShouldBe(48);

        // Metadata tokens follow declaration order, and the generator lays out the vtable in that order.
        string[] names = methods.OrderBy(m => m.MetadataToken).Select(m => m.Name).ToArray();
        names.ShouldBe(ExpectedBackupComponentsMethods);
    }

    // ---- IVssBackupComponents: signatures of the methods WinCopy calls ----

    [Fact]
    public void InitializeForBackupTakesBstrPointer()
    {
        ParameterInfo[] p = Parameters("InitializeForBackup");

        p.Length.ShouldBe(1);
        p[0].ParameterType.ShouldBe(typeof(nint));
    }

    [Fact]
    public void SetBackupStateUsesOneByteBoolsForCppBool()
    {
        ParameterInfo[] p = Parameters("SetBackupState");

        p.Length.ShouldBe(4);
        p[0].ParameterType.ShouldBe(typeof(bool));
        MarshalOf(p[0]).ShouldBe(UnmanagedType.U1);
        p[1].ParameterType.ShouldBe(typeof(bool));
        MarshalOf(p[1]).ShouldBe(UnmanagedType.U1);
        p[2].ParameterType.ShouldBe(typeof(int));
        p[3].ParameterType.ShouldBe(typeof(bool));
        MarshalOf(p[3]).ShouldBe(UnmanagedType.U1);
    }

    [Fact]
    public void SetContextTakesInt() =>
        Parameters("SetContext").Select(p => p.ParameterType).ShouldBe(new[] { typeof(int) });

    [Theory]
    [InlineData("GatherWriterMetadata")]
    [InlineData("PrepareForBackup")]
    [InlineData("DoSnapshotSet")]
    [InlineData("BackupComplete")]
    public void AsyncStartersReturnAsyncPointerOut(string method)
    {
        ParameterInfo[] p = Parameters(method);

        p.Length.ShouldBe(1);
        ShouldBeOut(p[0], typeof(nint));
    }

    [Fact]
    public void StartSnapshotSetReturnsSetIdOut()
    {
        ParameterInfo[] p = Parameters("StartSnapshotSet");

        p.Length.ShouldBe(1);
        ShouldBeOut(p[0], typeof(Guid));
    }

    [Fact]
    public void AddToSnapshotSetTakesWideVolumePathProviderIdAndSnapshotIdOut()
    {
        ParameterInfo[] p = Parameters("AddToSnapshotSet");

        p.Length.ShouldBe(3);
        p[0].ParameterType.ShouldBe(typeof(string));
        MarshalOf(p[0]).ShouldBe(UnmanagedType.LPWStr);
        p[1].ParameterType.ShouldBe(typeof(Guid));
        ShouldBeOut(p[2], typeof(Guid));
    }

    [Fact]
    public void DeleteSnapshotsUsesWin32BoolForForceDelete()
    {
        ParameterInfo[] p = Parameters("DeleteSnapshots");

        p.Length.ShouldBe(5);
        p[0].ParameterType.ShouldBe(typeof(Guid));
        p[1].ParameterType.ShouldBe(typeof(int));
        p[2].ParameterType.ShouldBe(typeof(bool));

        // BOOL in vss.h is a 4-byte Win32 BOOL, so it is Bool and not U1.
        MarshalOf(p[2]).ShouldBe(UnmanagedType.Bool);
        ShouldBeOut(p[3], typeof(int));
        ShouldBeOut(p[4], typeof(Guid));
    }

    [Fact]
    public void GetSnapshotPropertiesFillsVssSnapshotPropOut()
    {
        ParameterInfo[] p = Parameters("GetSnapshotProperties");

        p.Length.ShouldBe(2);
        p[0].ParameterType.ShouldBe(typeof(Guid));
        ShouldBeOut(p[1], typeof(VSS_SNAPSHOT_PROP));
    }

    [Fact]
    public void IsVolumeSupportedReturnsWin32BoolOut()
    {
        ParameterInfo[] p = Parameters("IsVolumeSupported");

        p.Length.ShouldBe(3);
        p[0].ParameterType.ShouldBe(typeof(Guid));
        p[1].ParameterType.ShouldBe(typeof(string));
        MarshalOf(p[1]).ShouldBe(UnmanagedType.LPWStr);

        // BOOL* in vss.h is a 4-byte Win32 BOOL, so it is Bool and not U1.
        ShouldBeOut(p[2], typeof(bool));
        MarshalOf(p[2]).ShouldBe(UnmanagedType.Bool);
    }

    [Fact]
    public void AbortBackupTakesNoParameters() =>
        Parameters("AbortBackup").ShouldBeEmpty();

    // ---- IVssAsync ----

    [Fact]
    public void AsyncIidMatchesVssHeader() =>
        typeof(IVssAsync).GUID.ShouldBe(Guid.Parse("507C37B4-CF5B-4e95-B0AF-14EB9767467E"));

    [Fact]
    public void AsyncHasCancelWaitQueryStatusInOrder()
    {
        MethodInfo[] methods = typeof(IVssAsync).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        methods.Length.ShouldBe(3);
        methods.OrderBy(m => m.MetadataToken).Select(m => m.Name).ToArray()
            .ShouldBe(ExpectedAsyncMethods);
    }

    [Fact]
    public void AsyncWaitTakesUnsignedMilliseconds() =>
        Parameters(typeof(IVssAsync), "Wait").Select(p => p.ParameterType).ShouldBe(new[] { typeof(uint) });

    [Fact]
    public void AsyncQueryStatusReturnsHresultThenReservedPointer()
    {
        ParameterInfo[] p = Parameters(typeof(IVssAsync), "QueryStatus");

        p.Length.ShouldBe(2);
        ShouldBeOut(p[0], typeof(int));
        p[1].ParameterType.ShouldBe(typeof(nint));
    }

    // ---- VSS_SNAPSHOT_PROP ----

    [Fact]
    public void SnapshotPropSizeIs128OnX64()
    {
        // The brief estimated 120 bytes. The natural x64 layout gives 128: the final int status is padded to the
        // 8-byte alignment of the structure. This is the size the C compiler produces for vss.h.
        Unsafe.SizeOf<VSS_SNAPSHOT_PROP>().ShouldBe(128);
    }

    [Fact]
    public void SnapshotPropDeviceObjectIsAtOffset40() =>
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.SnapshotDeviceObject)).ToInt32().ShouldBe(40);

    [Fact]
    public void SnapshotPropCreationTimestampIsAtOffset112() =>
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.CreationTimestamp)).ToInt32().ShouldBe(112);

    [Fact]
    public void SnapshotPropOtherOffsetsFollowTheHeader()
    {
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.SnapshotId)).ToInt32().ShouldBe(0);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.SnapshotSetId)).ToInt32().ShouldBe(16);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.SnapshotsCount)).ToInt32().ShouldBe(32);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.OriginalVolumeName)).ToInt32().ShouldBe(48);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.ExposedPath)).ToInt32().ShouldBe(80);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.ProviderId)).ToInt32().ShouldBe(88);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.SnapshotAttributes)).ToInt32().ShouldBe(104);
        Marshal.OffsetOf<VSS_SNAPSHOT_PROP>(nameof(VSS_SNAPSHOT_PROP.Status)).ToInt32().ShouldBe(120);
    }

    // ---- Constants and exports ----

    [Fact]
    public void VssConstantsMatchTheSdkHeaders()
    {
        VssConstants.VSS_CTX_BACKUP.ShouldBe(0);
        VssConstants.VSS_BT_COPY.ShouldBe(5);
        VssConstants.VSS_OBJECT_SNAPSHOT_SET.ShouldBe(2);
        VssConstants.VSS_S_ASYNC_FINISHED.ShouldBe(0x0004230A);
        VssConstants.VSS_S_ASYNC_PENDING.ShouldBe(0x00042309);
    }

    [Fact]
    public void VssApiDeclaresBothExports()
    {
        typeof(VssApi).GetMethod("CreateVssBackupComponentsInternal", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull();
        typeof(VssApi).GetMethod("VssFreeSnapshotProperties", BindingFlags.NonPublic | BindingFlags.Static)
            .ShouldNotBeNull();
    }

    private static ParameterInfo[] Parameters(string method) => Parameters(typeof(IVssBackupComponents), method);

    private static ParameterInfo[] Parameters(Type type, string method) =>
        type.GetMethod(method)!.GetParameters();

    private static UnmanagedType? MarshalOf(ParameterInfo parameter) =>
        parameter.GetCustomAttribute<MarshalAsAttribute>()?.Value;

    private static void ShouldBeOut(ParameterInfo parameter, Type element)
    {
        parameter.IsOut.ShouldBeTrue();
        parameter.ParameterType.IsByRef.ShouldBeTrue();
        parameter.ParameterType.GetElementType().ShouldBe(element);
    }
}
