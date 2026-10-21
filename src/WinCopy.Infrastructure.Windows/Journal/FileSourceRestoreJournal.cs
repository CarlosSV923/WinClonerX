using System.Text.Json;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Infrastructure.Windows.Journal;

/// <summary>
/// Source restore journal kept in one JSON file, <c>%ProgramData%\WinCopy\pending-source-restore.json</c> by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomic write.</b> The entry goes to a temporary file that is flushed to disk (write-through), and then it replaces the
/// journal file. A crash leaves either the previous journal or the new one, never a half-written file. The temporary file is
/// deleted when the write fails.
/// </para>
/// <para>
/// <b>Damaged files.</b> <see cref="Read"/> never throws for a damaged or unreadable file. It returns
/// <see cref="PendingSourceRestoreState.Invalid"/>, so the startup recovery reports the file and keeps it.
/// </para>
/// </remarks>
internal sealed class FileSourceRestoreJournal : ISourceRestoreJournal
{
    /// <summary>Name of the journal file inside the journal folder.</summary>
    public const string FileName = "pending-source-restore.json";

    private const string TempFileName = FileName + ".tmp";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _journalPath;
    private readonly string _tempPath;

    /// <summary>Creates the journal inside <paramref name="directory"/>. The folder is created on the first write.</summary>
    public FileSourceRestoreJournal(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = directory;
        _journalPath = Path.Combine(directory, FileName);
        _tempPath = Path.Combine(directory, TempFileName);
    }

    /// <summary>Creates the journal under the default folder, <c>%ProgramData%\WinCopy</c>.</summary>
    public static FileSourceRestoreJournal CreateDefault() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinCopy"));

    /// <summary>Full path of the journal file.</summary>
    public string JournalPath => _journalPath;

    /// <inheritdoc />
    public void Record(PendingSourceRestore entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(JournalFile.From(entry), JsonOptions);

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);

            try
            {
                using (var stream = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(_tempPath, _journalPath, overwrite: true);
            }
            catch
            {
                DeleteQuietly(_tempPath);
                throw;
            }
        }
    }

    /// <inheritdoc />
    public PendingSourceRestoreRead Read()
    {
        lock (_gate)
        {
            if (!File.Exists(_journalPath))
            {
                return PendingSourceRestoreRead.Absent;
            }

            try
            {
                var bytes = File.ReadAllBytes(_journalPath);
                var file = JsonSerializer.Deserialize<JournalFile>(bytes, JsonOptions)
                    ?? throw new InvalidDataException("El archivo del diario está vacío.");

                return PendingSourceRestoreRead.Present(file.ToEntry());
            }
            catch (Exception ex)
            {
                return PendingSourceRestoreRead.Invalid(ex.ToString());
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            // File.Delete does nothing when the file is already gone.
            File.Delete(_journalPath);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: a leftover temporary file is overwritten by the next write.
        }
    }

    /// <summary>Disk format of the entry. The version lets the format change later without misreading old files.</summary>
    public sealed class JournalFile
    {
        public int Version { get; set; }

        public Guid Partition { get; set; }

        public long OriginalLengthBytes { get; set; }

        public Guid Plan { get; set; }

        public DateTimeOffset RecordedAtUtc { get; set; }

        public static JournalFile From(PendingSourceRestore entry) => new()
        {
            Version = CurrentVersion,
            Partition = entry.Partition.Value,
            OriginalLengthBytes = entry.OriginalLength.Bytes,
            Plan = entry.Plan.Value,
            RecordedAtUtc = entry.RecordedAt.ToUniversalTime(),
        };

        /// <summary>Rebuilds the entry, validating it. Invalid values throw, and the caller reports the file as invalid.</summary>
        public PendingSourceRestore ToEntry()
        {
            if (Version != CurrentVersion)
            {
                throw new InvalidDataException($"Versión de diario no soportada: {Version}.");
            }

            return new PendingSourceRestore(
                new PartitionGuid(Partition),
                new ByteSize(OriginalLengthBytes),
                new PlanId(Plan),
                RecordedAtUtc);
        }
    }
}
