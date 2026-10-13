namespace WinCopy.Infrastructure.Windows.Interop;

/// <summary>
/// IOCTL and FSCTL control codes used by the raw disk and volume code.
/// Values are verified against <see cref="CtlCode"/>; do not change them.
/// </summary>
internal static class IoControlCodes
{
    // Disk (device type 7)
    public const uint IOCTL_DISK_GET_DRIVE_LAYOUT_EX = 0x00070050;
    public const uint IOCTL_DISK_SET_DRIVE_LAYOUT_EX = 0x0007C054;
    public const uint IOCTL_DISK_CREATE_DISK = 0x0007C058;
    public const uint IOCTL_DISK_DELETE_DRIVE_LAYOUT = 0x0007C100;
    public const uint IOCTL_DISK_UPDATE_PROPERTIES = 0x00070140;
    public const uint IOCTL_DISK_GET_DRIVE_GEOMETRY_EX = 0x000700A0;
    public const uint IOCTL_DISK_GET_LENGTH_INFO = 0x0007405C;
    public const uint IOCTL_DISK_GET_DISK_ATTRIBUTES = 0x000700F0;
    public const uint IOCTL_DISK_SET_DISK_ATTRIBUTES = 0x0007C0F4;

    // Storage (device type 0x2D)
    public const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x002D1080;

    // Volume (device type 0x56)
    public const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;

    // File system (device type 9)
    public const uint FSCTL_LOCK_VOLUME = 0x00090018;
    public const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
    public const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    public const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
    public const uint FSCTL_GET_VOLUME_BITMAP = 0x0009006F;
    public const uint FSCTL_EXTEND_VOLUME = 0x000900F0;

    /// <summary>Equivalent of the Win32 CTL_CODE macro.</summary>
    public static uint CtlCode(uint deviceType, uint function, uint method, uint access) =>
        (deviceType << 16) | (access << 14) | (function << 2) | method;
}
