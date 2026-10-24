using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Shouldly;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.IntegrationTests.Fixtures;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Spikes;

// Spike S1 (task T024, research R4). It answers two questions on a virtual disk:
//   (a) can a raw write succeed while the disk is offline (IOCTL_DISK_SET_DISK_ATTRIBUTES, non-persistent)?
//   (b) after the disk is back online and IOCTL_DISK_UPDATE_PROPERTIES runs, does Windows recognize a GPT that was
//       written by hand (protective MBR, primary and backup headers, one partition)?
// Needs an elevated process. Run it with:
//   dotnet test tests/WinClonerX.Infrastructure.IntegrationTests --filter "Spike=S1" --logger "console;verbosity=detailed"
// The only write handle comes from DiskWriteGuard. The read-back uses a GENERIC_READ handle on the same disk.
[Trait("Category", "Integration")]
[Trait("Spike", "S1")]
public sealed class OfflineRawWriteSpike
{
    private const int SectorSize = GptLayout.SectorSize;
    private const long PartitionFirstLba = 2048;
    private const long PartitionSectorCount = 64L * 1024 * 1024 / SectorSize;
    private const long PartitionLastLba = PartitionFirstLba + PartitionSectorCount - 1;
    private const int PatternLength = 1024 * 1024;
    private const long PatternOffset = 1024 * 1024;
    private const int DriveLayoutHeaderSize = 48;
    private const int PartitionInfoSize = IoControlConstants.PARTITION_INFORMATION_EX_SIZE;
    private const int DriveLayoutBufferSize = DriveLayoutHeaderSize + GptLayout.EntryCount * PartitionInfoSize;
    private const int NameOffsetInPartitionInfo = 72; // 32 (union start) + 16 (type) + 16 (id) + 8 (attributes)
    private const int NameBytes = 72;                 // 36 UTF-16 characters

    [Fact]
    public async Task OfflineWriteThenHandMadeGptIsRecognized()
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        var steps = new List<Step>();

        await using var fx = new VirtualDiskFixture();
        AttachedVirtualDisk disk = await fx.CreateAndAttachAsync(ByteSize.FromMiB(256), 512, 4096);

        using SafeDeviceHandle h = DiskWriteGuard.Default.OpenDiskForWrite(disk.DiskNumber);

        // 1. Offline, non-persistent. Then check the offline bit.
        Step offlineSet = SetOfflineState(h, offline: true);
        Step offlineCheck = offlineSet.Succeeded ? VerifyOfflineBit(h) : offlineSet;
        steps.Add(offlineSet);
        steps.Add(offlineCheck);

        // 2. Disk length, from IOCTL_DISK_GET_LENGTH_INFO.
        long lastLba = GetDiskLength(h) / SectorSize - 1;

        // 3. Raw writes while offline. Every write is recorded, including failures.
        byte[] pattern = BuildPattern();
        Guid diskGuid = Guid.NewGuid();
        Guid partitionGuid = Guid.NewGuid();
        byte[] mbr = GptLayout.BuildProtectiveMbr(lastLba);
        byte[] entries = GptLayout.BuildEntryArray(
            GptLayout.BasicDataType, partitionGuid, PartitionFirstLba, PartitionLastLba, "S1");
        uint entriesCrc = GptLayout.Crc32(entries);
        byte[] primaryHeader = GptLayout.BuildHeader(primary: true, lastLba, diskGuid, entriesCrc);
        byte[] backupHeader = GptLayout.BuildHeader(primary: false, lastLba, diskGuid, entriesCrc);

        var writes = new List<Step>();
        if (offlineCheck.Succeeded)
        {
            writes.Add(WriteAt(h, "data pattern at 1 MiB", PatternOffset, pattern));
            writes.Add(WriteAt(h, "protective MBR at LBA 0", 0, mbr));
            writes.Add(WriteAt(h, "primary GPT header at LBA 1", SectorSize, primaryHeader));
            writes.Add(WriteAt(h, "primary entries at LBA 2-33", 2L * SectorSize, entries));
            writes.Add(WriteAt(
                h,
                "backup entries at LBA last-32",
                (lastLba - GptLayout.EntryArraySectors) * SectorSize,
                entries));
            writes.Add(WriteAt(h, "backup GPT header at last LBA", lastLba * SectorSize, backupHeader));
        }

        steps.AddRange(writes);
        Step? firstWriteFailure = writes.Find(w => !w.Succeeded);
        bool offlineWriteOk = offlineCheck.Succeeded && writes.Count > 0 && firstWriteFailure is null;
        int offlineWriteError = !offlineCheck.Succeeded
            ? offlineCheck.Win32Error
            : (firstWriteFailure?.Win32Error ?? 0);

        // 4. Back online, then IOCTL_DISK_UPDATE_PROPERTIES.
        Step online = SetOfflineState(h, offline: false);
        Step update = UpdateProperties(h);
        steps.Add(online);
        steps.Add(update);

        // 5. Read the layout back.
        byte[] layoutBuffer = new byte[DriveLayoutBufferSize];
        bool layoutRead = TryGetDriveLayout(h, layoutBuffer, out int layoutError);
        steps.Add(new Step("IOCTL_DISK_GET_DRIVE_LAYOUT_EX", layoutRead, layoutError));
        DriveLayout layout = ParseDriveLayout(layoutBuffer);
        bool gptRecognized = layoutRead
            && layout.PartitionStyle == 1
            && layout.BasicDataPartitionFound
            && layout.DiskId == diskGuid;

        // 6. Read the 1 MiB pattern back through a read-only handle.
        Step readBack = ReadBackPattern(disk.DiskNumber, pattern);
        steps.Add(readBack);

        // Summary. It is written before the asserts, so it is always visible.
        foreach (Step step in steps)
        {
            Report(output, step.Describe());
        }

        Report(output, $"Layout: PartitionStyle={layout.PartitionStyle} (1 = GPT), DiskId={layout.DiskId}, partitions: {layout.Partitions}");
        Report(output, $"Expected DiskId={diskGuid}, partition type={GptLayout.BasicDataType}, offset={PartitionFirstLba * SectorSize}, length={PartitionSectorCount * SectorSize}");

        string offlineText = offlineWriteOk ? "OK" : $"FAILED (error {offlineWriteError})";
        string gptText = gptRecognized ? "YES" : "NO";
        Report(output, $"S1 RESULT: offline raw write = {offlineText}; GPT recognized = {gptText}");

        // Asserts. Each failure message names the step that failed.
        offlineCheck.Succeeded.ShouldBeTrue($"the disk did not go offline: {offlineCheck.Describe()}");
        firstWriteFailure.ShouldBeNull($"a raw write failed while offline: {firstWriteFailure?.Describe()}");
        online.Succeeded.ShouldBeTrue($"the disk did not come back online: {online.Describe()}");
        update.Succeeded.ShouldBeTrue($"IOCTL_DISK_UPDATE_PROPERTIES failed: {update.Describe()}");
        layoutRead.ShouldBeTrue($"IOCTL_DISK_GET_DRIVE_LAYOUT_EX failed with error {layoutError}");
        layout.PartitionStyle.ShouldBe(1u, $"partition style should be GPT (1), was {layout.PartitionStyle}");
        layout.BasicDataPartitionFound.ShouldBeTrue(
            $"no partition with the written type, offset and length. Found: {layout.Partitions}");
        layout.DiskId.ShouldBe(diskGuid, "the DiskId reported by Windows should equal the written disk GUID");
        readBack.Succeeded.ShouldBeTrue($"read-back of the pattern failed: {readBack.Describe()}");
    }

    private sealed record Step(string Name, bool Succeeded, int Win32Error, string Detail = "")
    {
        public string Describe()
        {
            string result = Succeeded ? "OK" : $"FAILED (error {Win32Error})";
            return string.IsNullOrEmpty(Detail) ? $"{Name}: {result}" : $"{Name}: {result} [{Detail}]";
        }
    }

    private sealed record DriveLayout(uint PartitionStyle, Guid DiskId, bool BasicDataPartitionFound, string Partitions);

    private static void Report(ITestOutputHelper? output, string line)
    {
        if (output is null)
        {
            Console.WriteLine(line);
        }
        else
        {
            output.WriteLine(line);
        }
    }

    private static byte[] BuildPattern()
    {
        byte[] pattern = new byte[PatternLength];
        for (int i = 0; i < pattern.Length; i++)
        {
            pattern[i] = (byte)((i * 31) % 251);
        }

        return pattern;
    }

    private static unsafe Step SetOfflineState(SafeDeviceHandle h, bool offline)
    {
        var input = new SET_DISK_ATTRIBUTES
        {
            Version = (uint)sizeof(SET_DISK_ATTRIBUTES),
            Persist = 0,
            Attributes = offline ? IoControlConstants.DISK_ATTRIBUTE_OFFLINE : 0UL,
            AttributesMask = IoControlConstants.DISK_ATTRIBUTE_OFFLINE,
        };

        bool ok = Kernel32.DeviceIoControl(
            h,
            IoControlCodes.IOCTL_DISK_SET_DISK_ATTRIBUTES,
            &input,
            (uint)sizeof(SET_DISK_ATTRIBUTES),
            null,
            0,
            out _,
            IntPtr.Zero);

        string name = offline ? "IOCTL_DISK_SET_DISK_ATTRIBUTES (offline, non-persistent)" : "IOCTL_DISK_SET_DISK_ATTRIBUTES (online)";
        return new Step(name, ok, ok ? 0 : Marshal.GetLastPInvokeError());
    }

    private static unsafe Step VerifyOfflineBit(SafeDeviceHandle h)
    {
        var output = new GET_DISK_ATTRIBUTES();
        bool ok = Kernel32.DeviceIoControl(
            h,
            IoControlCodes.IOCTL_DISK_GET_DISK_ATTRIBUTES,
            null,
            0,
            &output,
            (uint)sizeof(GET_DISK_ATTRIBUTES),
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            return new Step("IOCTL_DISK_GET_DISK_ATTRIBUTES", false, Marshal.GetLastPInvokeError());
        }

        bool offlineBit = (output.Attributes & IoControlConstants.DISK_ATTRIBUTE_OFFLINE) != 0;
        return new Step(
            "IOCTL_DISK_GET_DISK_ATTRIBUTES (offline bit set)",
            offlineBit,
            0,
            $"attributes=0x{output.Attributes:X}");
    }

    private static unsafe long GetDiskLength(SafeDeviceHandle h)
    {
        var info = new GET_LENGTH_INFORMATION();
        bool ok = Kernel32.DeviceIoControl(
            h,
            IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO,
            null,
            0,
            &info,
            (uint)sizeof(GET_LENGTH_INFORMATION),
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "IOCTL_DISK_GET_LENGTH_INFO failed.");
        }

        return info.Length;
    }

    private static unsafe Step UpdateProperties(SafeDeviceHandle h)
    {
        bool ok = Kernel32.DeviceIoControl(
            h,
            IoControlCodes.IOCTL_DISK_UPDATE_PROPERTIES,
            null,
            0,
            null,
            0,
            out _,
            IntPtr.Zero);

        return new Step("IOCTL_DISK_UPDATE_PROPERTIES", ok, ok ? 0 : Marshal.GetLastPInvokeError());
    }

    private static unsafe bool TryGetDriveLayout(SafeDeviceHandle h, byte[] buffer, out int error)
    {
        fixed (byte* pointer = buffer)
        {
            bool ok = Kernel32.DeviceIoControl(
                h,
                IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                null,
                0,
                pointer,
                (uint)buffer.Length,
                out _,
                IntPtr.Zero);

            error = ok ? 0 : Marshal.GetLastPInvokeError();
            return ok;
        }
    }

    private static DriveLayout ParseDriveLayout(byte[] buffer)
    {
        DRIVE_LAYOUT_INFORMATION_EX header = MemoryMarshal.Read<DRIVE_LAYOUT_INFORMATION_EX>(buffer);
        int count = (int)Math.Min(header.PartitionCount, (uint)GptLayout.EntryCount);

        bool found = false;
        var described = new List<string>();
        for (int i = 0; i < count; i++)
        {
            int offset = DriveLayoutHeaderSize + i * PartitionInfoSize;
            PARTITION_INFORMATION_EX part = MemoryMarshal.Read<PARTITION_INFORMATION_EX>(buffer.AsSpan(offset));
            string name = Encoding.Unicode.GetString(buffer, offset + NameOffsetInPartitionInfo, NameBytes).TrimEnd('\0');

            found |= part.Gpt.PartitionType == GptLayout.BasicDataType
                && part.StartingOffset == PartitionFirstLba * SectorSize
                && part.PartitionLength == PartitionSectorCount * SectorSize;

            described.Add(
                $"#{part.PartitionNumber} type={part.Gpt.PartitionType} offset={part.StartingOffset} " +
                $"length={part.PartitionLength} name='{name}'");
        }

        string partitions = described.Count == 0 ? "(none)" : string.Join("; ", described);
        return new DriveLayout(header.PartitionStyle, header.Gpt.DiskId, found, partitions);
    }

    private static unsafe Step WriteAt(SafeDeviceHandle h, string name, long offset, byte[] data)
    {
        using AlignedBuffer buffer = new(data.Length, (nuint)SectorSize);
        data.AsSpan().CopyTo(buffer.Span);

        if (!SpikeNative.SetFilePointerEx(h, offset, out _, SpikeNative.FILE_BEGIN))
        {
            return new Step(name, false, Marshal.GetLastPInvokeError(), $"seek to {offset} failed");
        }

        bool ok = SpikeNative.WriteFile(h, (byte*)buffer.Address, (uint)buffer.Length, out uint written, IntPtr.Zero);
        int error = ok ? 0 : Marshal.GetLastPInvokeError();
        bool complete = ok && written == buffer.Length;
        return new Step(name, complete, error, $"offset {offset}, {data.Length} bytes, written {written}");
    }

    private static unsafe bool ReadAt(SafeDeviceHandle h, long offset, AlignedBuffer buffer, out int error)
    {
        if (!SpikeNative.SetFilePointerEx(h, offset, out _, SpikeNative.FILE_BEGIN))
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        bool ok = SpikeNative.ReadFile(h, (byte*)buffer.Address, (uint)buffer.Length, out uint read, IntPtr.Zero);
        if (!ok)
        {
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        error = 0;
        return read == buffer.Length;
    }

    private static Step ReadBackPattern(int diskNumber, byte[] expected)
    {
        const string name = "read back 1 MiB at 1 MiB (GENERIC_READ handle)";

        // Read-only handle. It never requests GENERIC_WRITE.
        using SafeDeviceHandle reader = Kernel32.CreateFileW(
            PhysicalDisk.PathFor(diskNumber),
            Kernel32.GENERIC_READ,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_NO_BUFFERING,
            IntPtr.Zero);

        if (reader.IsInvalid)
        {
            return new Step(name, false, Marshal.GetLastPInvokeError(), "could not open read-only");
        }

        using AlignedBuffer buffer = new(PatternLength, (nuint)SectorSize);
        if (!ReadAt(reader, PatternOffset, buffer, out int error))
        {
            return new Step(name, false, error, "short or failed read");
        }

        bool same = expected.AsSpan().SequenceEqual(buffer.Span);
        return new Step(name, same, 0, same ? "bytes equal" : "bytes differ");
    }
}
