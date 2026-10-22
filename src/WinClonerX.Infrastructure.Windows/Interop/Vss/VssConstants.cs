namespace WinClonerX.Infrastructure.Windows.Interop.Vss;

// Values from the Windows SDK headers vss.h and winerror.h. They are part of the platform ABI and do not change.
internal static class VssConstants
{
    /// <summary>VSS_CTX_BACKUP: the backup context, the only one WinClonerX uses.</summary>
    public const int VSS_CTX_BACKUP = 0;

    /// <summary>VSS_BT_COPY: a copy backup. It does not change the backup history of other applications.</summary>
    public const int VSS_BT_COPY = 5;

    /// <summary>VSS_OBJECT_SNAPSHOT_SET: the object type used when deleting a whole snapshot set.</summary>
    public const int VSS_OBJECT_SNAPSHOT_SET = 2;

    /// <summary>VSS_S_ASYNC_FINISHED: the asynchronous operation completed successfully.</summary>
    public const int VSS_S_ASYNC_FINISHED = 0x0004230A;

    /// <summary>VSS_S_ASYNC_PENDING: the asynchronous operation has not finished yet.</summary>
    public const int VSS_S_ASYNC_PENDING = 0x00042309;

    /// <summary>INFINITE: wait without a timeout.</summary>
    public const uint INFINITE = 0xFFFFFFFF;
}
