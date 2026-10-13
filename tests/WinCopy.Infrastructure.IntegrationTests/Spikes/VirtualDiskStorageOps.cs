using System.Diagnostics;
using System.Text.Json;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

/// <summary>Runs one PowerShell script. Injected so that a test can prove that a refusal happens before any process starts.</summary>
internal interface IPowerShellRunner
{
    PowerShellResult Run(string script);
}

internal sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Starts <c>powershell.exe -NoProfile -NonInteractive -Command</c> and captures both output streams.</summary>
internal sealed class ProcessPowerShellRunner : IPowerShellRunner
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(3);

    public PowerShellResult Run(string script)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("powershell.exe did not start.");
        process.StandardInput.Close();
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)_timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException("powershell.exe did not finish in time.");
        }

        process.WaitForExit();
        return new PowerShellResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }
}

/// <summary>Thrown before a storage command runs when its target is not a safe virtual disk.</summary>
internal sealed class UnsafeStorageTargetException : InvalidOperationException
{
    public UnsafeStorageTargetException(string message)
        : base(message)
    {
    }
}

/// <summary>Thrown when a PowerShell storage command fails or answers with output that cannot be read.</summary>
internal sealed class StorageCommandException : InvalidOperationException
{
    public StorageCommandException(string message)
        : base(message)
    {
    }
}

/// <summary>Partition facts read back from PowerShell.</summary>
internal sealed record PartitionInfo(
    int DiskNumber,
    int PartitionNumber,
    char? DriveLetter,
    long OffsetBytes,
    long SizeBytes,
    IReadOnlyList<string> AccessPaths);

/// <summary>BitLocker state of a volume, as reported by <c>Get-BitLockerVolume</c>.</summary>
internal sealed record BitLockerStatus(string VolumeStatus, string ProtectionStatus, int EncryptionPercentage);

/// <summary>
/// The only way the spike S3 runs Storage or BitLocker commands. Every public operation first checks the target with
/// <see cref="AssertSafeTarget"/> and throws before anything runs when a check fails. A drive letter is accepted only
/// after <c>Get-Partition -DriveLetter</c> maps it to the verified disk number.
/// </summary>
/// <remarks>
/// Safe target means all of the following: the disk number is in <see cref="VirtualDiskRegistry"/> (attached by a
/// <see cref="VirtualDiskFixture"/> of this process), its bus type is <c>FileBackedVirtual</c>, and it holds neither the
/// running Windows volume nor the system partition.
/// </remarks>
internal sealed class VirtualDiskStorageOps
{
    private const string PartitionJsonScript =
        "[pscustomobject]@{ DiskNumber = [int]$p.DiskNumber; PartitionNumber = [int]$p.PartitionNumber; " +
        "DriveLetter = [string]$p.DriveLetter; OffsetBytes = [long]$p.Offset; SizeBytes = [long]$p.Size; " +
        "AccessPaths = @($p.AccessPaths) } | ConvertTo-Json -Compress";

    private const string BitLockerJsonScript =
        "[pscustomobject]@{ VolumeStatus = [string]$v.VolumeStatus; ProtectionStatus = [string]$v.ProtectionStatus; " +
        "EncryptionPercentage = [int]$v.EncryptionPercentage } | ConvertTo-Json -Compress";

    private const string ErrorPreamble =
        "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; $WarningPreference = 'SilentlyContinue'; try { ";

    private const string ErrorEpilogue = " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";

    private readonly IPowerShellRunner _runner;
    private readonly IVolumeExtentsReader _reader;

    public VirtualDiskStorageOps(IPowerShellRunner runner, IVolumeExtentsReader reader)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(reader);

        _runner = runner;
        _reader = reader;
    }

    /// <summary>The operations used by the spike, with the real PowerShell runner and the real volume reader.</summary>
    public static VirtualDiskStorageOps Default { get; } =
        new(new ProcessPowerShellRunner(), new WindowsVolumeExtentsReader());

    /// <summary>Throws <see cref="UnsafeStorageTargetException"/> unless <paramref name="diskNumber"/> is a safe target.</summary>
    public void AssertSafeTarget(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        if (!VirtualDiskRegistry.Contains(diskNumber))
        {
            throw Refuse(diskNumber, "it was not attached by a VirtualDiskFixture of this process");
        }

        STORAGE_BUS_TYPE busType = _reader.BusTypeOf(diskNumber);
        if (busType != STORAGE_BUS_TYPE.BusTypeFileBackedVirtual)
        {
            throw Refuse(diskNumber, $"its bus type is {busType}, not FileBackedVirtual");
        }

        uint number = (uint)diskNumber;
        if (_reader.DiskNumbersOf(_reader.WindowsVolumePath()).Contains(number))
        {
            throw Refuse(diskNumber, "it holds the running Windows volume");
        }

        if (_reader.DiskNumbersOf(_reader.SystemPartitionVolumePath()).Contains(number))
        {
            throw Refuse(diskNumber, "it holds the system partition");
        }
    }

    /// <summary>Throws unless the volume at <paramref name="volumePath"/> lies entirely on <paramref name="diskNumber"/>.</summary>
    public void AssertVolumeOnDisk(string volumePath, int diskNumber)
    {
        AssertSafeTarget(diskNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(volumePath);

        IReadOnlySet<uint> disks = _reader.DiskNumbersOf(volumePath);
        if (disks.Count != 1 || !disks.Contains((uint)diskNumber))
        {
            throw new UnsafeStorageTargetException(
                $"Refusing to lock volume '{volumePath}': it does not lie entirely on disk {diskNumber}.");
        }
    }

    /// <summary>Throws unless <c>Get-Partition -DriveLetter</c> maps <paramref name="letter"/> to <paramref name="diskNumber"/>.</summary>
    public void AssertLetterOnDisk(char letter, int diskNumber)
    {
        AssertSafeTarget(diskNumber);
        RequireDriveLetter(letter);

        string stdout = RunChecked(
            "$p = @(Get-Partition -DriveLetter '" + letter + "'); " +
            "if ($p.Count -ne 1) { throw 'expected exactly one partition for the drive letter' }; " +
            "[pscustomobject]@{ DiskNumber = [int]$p[0].DiskNumber } | ConvertTo-Json -Compress");

        using JsonDocument document = JsonDocument.Parse(LastLine(stdout));
        int actual = document.RootElement.GetProperty("DiskNumber").GetInt32();
        if (actual != diskNumber)
        {
            throw new UnsafeStorageTargetException(
                $"Refusing to act on drive {letter}: it is on disk {actual}, not disk {diskNumber}.");
        }
    }

    public void InitializeGpt(int diskNumber)
    {
        AssertSafeTarget(diskNumber);
        RunChecked($"Initialize-Disk -Number {diskNumber} -PartitionStyle GPT -Confirm:$false");
    }

    public PartitionInfo CreateMaximumPartitionWithLetter(int diskNumber)
    {
        AssertSafeTarget(diskNumber);
        string stdout = RunChecked(
            $"$p = New-Partition -DiskNumber {diskNumber} -UseMaximumSize -AssignDriveLetter; " +
            $"$p = Get-Partition -DiskNumber {diskNumber} -PartitionNumber $p.PartitionNumber; " +
            PartitionJsonScript);
        return CheckedPartition(stdout, diskNumber);
    }

    /// <summary>Creates a partition of exactly <paramref name="sizeBytes"/>. It gets no drive letter.</summary>
    public PartitionInfo CreatePartitionWithoutLetter(int diskNumber, long sizeBytes)
    {
        AssertSafeTarget(diskNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeBytes);

        string stdout = RunChecked(
            $"$p = New-Partition -DiskNumber {diskNumber} -Size {sizeBytes}; " +
            $"$p = Get-Partition -DiskNumber {diskNumber} -PartitionNumber $p.PartitionNumber; " +
            PartitionJsonScript);
        return CheckedPartition(stdout, diskNumber);
    }

    public PartitionInfo AssignDriveLetter(int diskNumber, int partitionNumber)
    {
        AssertSafeTarget(diskNumber);
        string stdout = RunChecked(
            $"Add-PartitionAccessPath -DiskNumber {diskNumber} -PartitionNumber {partitionNumber} -AssignDriveLetter; " +
            $"$p = Get-Partition -DiskNumber {diskNumber} -PartitionNumber {partitionNumber}; " +
            PartitionJsonScript);
        return CheckedPartition(stdout, diskNumber);
    }

    public PartitionInfo GetPartition(int diskNumber, int partitionNumber)
    {
        AssertSafeTarget(diskNumber);
        string stdout = RunChecked(
            $"$p = Get-Partition -DiskNumber {diskNumber} -PartitionNumber {partitionNumber}; " +
            PartitionJsonScript);
        return CheckedPartition(stdout, diskNumber);
    }

    /// <summary>Cleanup only. Removes the drive letter of a partition of a safe target.</summary>
    public void RemoveDriveLetter(int diskNumber, int partitionNumber, char letter)
    {
        AssertSafeTarget(diskNumber);
        RequireDriveLetter(letter);
        AssertLetterOnDisk(letter, diskNumber);

        RunChecked(
            $"Remove-PartitionAccessPath -DiskNumber {diskNumber} -PartitionNumber {partitionNumber} " +
            $"-AccessPath '{letter}:\\' -Confirm:$false");
    }

    /// <summary>Formats the partition behind <paramref name="letter"/> as NTFS. The letter must be on <paramref name="diskNumber"/>.</summary>
    public void FormatNtfs(int diskNumber, char letter)
    {
        AssertSafeTarget(diskNumber);
        RequireDriveLetter(letter);
        AssertLetterOnDisk(letter, diskNumber);

        RunChecked($"Format-Volume -DriveLetter '{letter}' -FileSystem NTFS -Confirm:$false -Force | Out-Null");
    }

    /// <summary>Turns on BitLocker with a password protector and used-space-only encryption.</summary>
    public void EnableBitLockerWithPassword(int diskNumber, char letter, string password)
    {
        AssertSafeTarget(diskNumber);
        RequireDriveLetter(letter);
        AssertLetterOnDisk(letter, diskNumber);
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (!password.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new ArgumentException("The test password may only contain letters, digits and '-'.", nameof(password));
        }

        RunChecked(
            $"Enable-BitLocker -MountPoint '{letter}:' -PasswordProtector " +
            $"-Password (ConvertTo-SecureString '{password}' -AsPlainText -Force) " +
            "-UsedSpaceOnly -SkipHardwareTest | Out-Null");
    }

    public BitLockerStatus GetBitLockerStatus(int diskNumber, char letter)
    {
        AssertSafeTarget(diskNumber);
        RequireDriveLetter(letter);
        AssertLetterOnDisk(letter, diskNumber);

        string stdout = RunChecked($"$v = Get-BitLockerVolume -MountPoint '{letter}:'; " + BitLockerJsonScript);

        using JsonDocument document = JsonDocument.Parse(LastLine(stdout));
        JsonElement root = document.RootElement;
        return new BitLockerStatus(
            ReadString(root, "VolumeStatus") ?? string.Empty,
            ReadString(root, "ProtectionStatus") ?? string.Empty,
            root.GetProperty("EncryptionPercentage").GetInt32());
    }

    private static UnsafeStorageTargetException Refuse(int diskNumber, string reason) =>
        new($"Refusing storage command on disk {diskNumber}: {reason}.");

    private static void RequireDriveLetter(char letter)
    {
        if (!char.IsAsciiLetterUpper(letter))
        {
            throw new ArgumentOutOfRangeException(nameof(letter), letter, "The drive letter must be an upper-case ASCII letter.");
        }
    }

    private static PartitionInfo CheckedPartition(string stdout, int diskNumber)
    {
        using JsonDocument document = JsonDocument.Parse(LastLine(stdout));
        JsonElement root = document.RootElement;

        int actualDisk = root.GetProperty("DiskNumber").GetInt32();
        if (actualDisk != diskNumber)
        {
            throw new StorageCommandException($"PowerShell reported disk {actualDisk} for a command on disk {diskNumber}.");
        }

        return new PartitionInfo(
            actualDisk,
            root.GetProperty("PartitionNumber").GetInt32(),
            ParseDriveLetter(ReadString(root, "DriveLetter")),
            root.GetProperty("OffsetBytes").GetInt64(),
            root.GetProperty("SizeBytes").GetInt64(),
            ReadStrings(root, "AccessPaths"));
    }

    private static char? ParseDriveLetter(string? text) =>
        !string.IsNullOrEmpty(text) && char.IsAsciiLetter(text[0])
            ? char.ToUpperInvariant(text[0])
            : (char?)null;

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<string> ReadStrings(JsonElement root, string name)
    {
        var values = new List<string>();
        if (!root.TryGetProperty(name, out JsonElement value))
        {
            return values;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            values.Add(value.GetString() ?? string.Empty);
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray())
            {
                values.Add(item.GetString() ?? string.Empty);
            }
        }

        return values;
    }

    private static string LastLine(string stdout)
    {
        string[] lines = stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0
            ? lines[^1]
            : throw new StorageCommandException("PowerShell printed no output where a result was expected.");
    }

    private string RunChecked(string body)
    {
        PowerShellResult result = _runner.Run(ErrorPreamble + body + ErrorEpilogue);
        if (result.ExitCode != 0)
        {
            // The script text is not reported: some commands carry the test password.
            throw new StorageCommandException(
                $"A PowerShell storage command exited with {result.ExitCode}: {result.StandardError.Trim()}");
        }

        return result.StandardOutput;
    }
}
