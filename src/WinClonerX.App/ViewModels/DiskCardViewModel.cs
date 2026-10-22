using CommunityToolkit.Mvvm.ComponentModel;
using WinClonerX.App.Localization;
using WinClonerX.App.Presentation;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.App.ViewModels;

/// <summary>
/// What a disk card shows: model, serial number, capacity, connection and its partition bar. Used for the source disk
/// (not selectable) and as the base of <see cref="TargetDiskItemViewModel"/>, which adds eligibility and selection.
/// </summary>
public partial class DiskCardViewModel : ObservableObject
{
    public DiskCardViewModel(Disk disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        Disk = disk;
        DiskId = disk.Id;
        Model = disk.Model;
        SerialNumber = disk.SerialNumber;
        Capacity = disk.Size;
        BusLabel = Labels.Bus(disk.Bus);

        var segments = disk.Partitions
            .Select(partition => new PartitionSegment(
                partition.Role,
                new ByteSize(partition.Extent.Length),
                partition.Volume?.Used,
                Labels.Role(partition.Role)))
            .ToArray();
        PartitionBar = new PartitionBarData(segments, disk.Size);
        HelpText = PartitionBar.AccessibleText;

        // An empty or all-zero serial (some USB bridges) is not shown as if it were a real number.
        var serialShown = TargetConfirmation.GetSource(disk) is not ConfirmationSource.SerialNumber
            ? Loc.Get("Disk_Serial_NotReported")
            : SerialNumber.Trim();
        SerialText = Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Serial"), serialShown);
        CapacityText = Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Capacity"), SizeText.Format(Capacity));
        BusText = Loc.Format("Disk_Field_Format", Loc.Get("Disk_Label_Connection"), BusLabel);
        var source = TargetConfirmation.GetSource(disk);
        var identifier = disk.Id.DiskGuid == Guid.Empty
            ? source switch
            {
                ConfirmationSource.Model => disk.Model.Trim(),
                ConfirmationSource.DiskNumber => $"DISCO{disk.Id.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                _ => Loc.Get("Disk_Guid_NotReported"),
            }
            : GroupedGuid(disk.Id.DiskGuid);
        var identifierLabel = disk.Id.DiskGuid == Guid.Empty && source is ConfirmationSource.Model or ConfirmationSource.DiskNumber
            ? Loc.Get("Disk_Label_FallbackIdentifier")
            : Loc.Get("Disk_Label_Guid");
        GuidText = Loc.Format("Disk_Field_Format", identifierLabel, identifier);
        AutomationName = Loc.Format("Disk_Automation_Name", Model, SerialText, CapacityText, BusText);
    }

    /// <summary>The disk GUID in the usual grouped form, 8-4-4-4-12 characters.</summary>
    public static string GroupedGuid(Guid diskGuid) => diskGuid.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The domain disk this card shows. Used by the confirmation step to erase exactly this disk.</summary>
    public Disk Disk { get; }

    /// <summary>Identifier of the disk.</summary>
    public DiskId DiskId { get; }

    public string Model { get; }

    public string SerialNumber { get; }

    public ByteSize Capacity { get; }

    /// <summary>Localized connection name: internal, USB or virtual.</summary>
    public string BusLabel { get; }

    /// <summary>"Serial number: ..." as shown on the card.</summary>
    public string SerialText { get; }

    /// <summary>"Capacity: ..." as shown on the card.</summary>
    public string CapacityText { get; }

    /// <summary>"Connection: ..." as shown on the card.</summary>
    public string BusText { get; }

    /// <summary>"Disk identifier: ..." (the GUID, grouped) as shown on the card.</summary>
    public string GuidText { get; }

    /// <summary>Partitions and the disk size they are drawn against.</summary>
    public PartitionBarData PartitionBar { get; }

    /// <summary>Accessible name of the card: model, serial, capacity and connection.</summary>
    public string AutomationName { get; }

    /// <summary>Accessible help text: status, reasons and what can be done with the card.</summary>
    public string HelpText { get; protected set; } = string.Empty;

    /// <summary>Whether the person can select this card. Only eligible destinations are selectable.</summary>
    public bool IsSelectable { get; protected set; }

    /// <summary>Whether the card is shown in the disabled state (a destination that is not eligible).</summary>
    public bool IsDisabled { get; protected set; }

    /// <summary>Whether this disk can be used as the destination. False for the source disk.</summary>
    public bool IsEligible { get; protected set; }

    /// <summary>Whether this card is the selected destination.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Badge text ("Eligible" or "Not eligible"). Null when the card has no eligibility badge.</summary>
    public string? StatusText { get; protected set; }

    /// <summary>Localized reasons, each prefixed with its severity. Empty when there is nothing to explain.</summary>
    public IReadOnlyList<string> Reasons { get; protected set; } = [];

    /// <summary>Strategy hint for eligible destinations ("Windows will be extended"). Null otherwise.</summary>
    public string? StrategyText { get; protected set; }

    /// <summary>"Missing X" for destinations that are too small. Null otherwise.</summary>
    public string? ShortfallText { get; protected set; }
}
