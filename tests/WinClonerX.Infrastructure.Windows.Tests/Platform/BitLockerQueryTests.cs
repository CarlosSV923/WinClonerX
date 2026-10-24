using System.Globalization;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Domain.Inventory;
using WinClonerX.Infrastructure.Windows.Platform;
using WinClonerX.Infrastructure.Windows.Storage;
using WinClonerX.Infrastructure.Windows.Tests.Storage;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Platform;

// The WMI rows are replaced by a fake. No test queries BitLocker.
public sealed class BitLockerQueryTests
{
    private const string Volume1 = @"\\?\Volume{1c5367fb-d302-425a-9f83-460d2490de24}\";
    private const string Volume2 = @"\\?\Volume{2b35a440-d4bd-4bc4-a836-bca5304e1a3b}\";

    // -1 stands for a property the WMI class did not return.
    [Theory]
    [InlineData(1, 0, EncryptionState.BitLockerOn)]
    [InlineData(1, 1, EncryptionState.BitLockerOn)]
    [InlineData(1, -1, EncryptionState.BitLockerOn)]
    [InlineData(0, 0, EncryptionState.None)]
    [InlineData(0, 1, EncryptionState.BitLockerSuspended)]
    [InlineData(0, 2, EncryptionState.BitLockerSuspended)]
    [InlineData(0, 3, EncryptionState.BitLockerSuspended)]
    [InlineData(0, 4, EncryptionState.BitLockerSuspended)]
    [InlineData(0, 5, EncryptionState.BitLockerSuspended)]
    [InlineData(0, -1, EncryptionState.Unknown)]
    [InlineData(2, 0, EncryptionState.Unknown)]
    [InlineData(2, 1, EncryptionState.Unknown)]
    [InlineData(5, 0, EncryptionState.Unknown)]
    [InlineData(-1, 0, EncryptionState.Unknown)]
    [InlineData(-1, -1, EncryptionState.Unknown)]
    public void MapsEveryCombinationOfTheStatusCodes(int protection, int conversion, EncryptionState expected) =>
        BitLockerQuery.MapStatus(ToNullable(protection), ToNullable(conversion)).ShouldBe(expected);

    [Fact]
    public void EveryCombinationOfTheDocumentedCodesFollowsTheTable()
    {
        uint?[] protections = [null, 0, 1, 2, 3];
        uint?[] conversions = [null, 0, 1, 2, 3, 4, 5];

        foreach (uint? protection in protections)
        {
            foreach (uint? conversion in conversions)
            {
                EncryptionState expected = protection switch
                {
                    1 => EncryptionState.BitLockerOn,
                    0 => conversion switch
                    {
                        null => EncryptionState.Unknown,
                        0 => EncryptionState.None,
                        _ => EncryptionState.BitLockerSuspended,
                    },
                    _ => EncryptionState.Unknown,
                };

                BitLockerQuery.MapStatus(protection, conversion)
                    .ShouldBe(expected, $"protection={Show(protection)}, conversion={Show(conversion)}");
            }
        }
    }

    [Fact]
    public void RowsMatchTheRequestedVolumeIgnoringTrailingBackslashAndCase()
    {
        var source = new FakeBitLockerWmi(
        [
            new BitLockerVolumeRecord(Volume1.TrimEnd('\\').ToUpperInvariant(), 1, 1),
        ]);

        var states = new BitLockerQuery(source).GetStates([Volume1]);

        states[Key(Volume1)].ShouldBe(EncryptionState.BitLockerOn);
    }

    [Fact]
    public void AVolumeWithoutARowIsUnknownWhileTheNamespaceAnswers()
    {
        var source = new FakeBitLockerWmi([new BitLockerVolumeRecord(Volume1, 0, 0)]);

        var states = new BitLockerQuery(source).GetStates([Volume1, Volume2]);

        states[Key(Volume1)].ShouldBe(EncryptionState.None);
        states[Key(Volume2)].ShouldBe(EncryptionState.Unknown);
    }

    [Fact]
    public void AMissingNamespaceReportsEveryVolumeAsNoneAndWarnsOnce()
    {
        var warnings = new List<string>();
        var source = new FakeBitLockerWmi(failure: new PlatformException(PlatformErrorCode.DeviceNotFound, "no namespace"));

        var states = new BitLockerQuery(source, warnings.Add).GetStates([Volume1, Volume2]);

        states.Values.ShouldAllBe(state => state == EncryptionState.None);
        states.Count.ShouldBe(2);
        warnings.Count.ShouldBe(1);
    }

    [Fact]
    public void AccessDeniedIsNotTurnedIntoNoneBecauseItIsNotAMissingNamespace()
    {
        var warnings = new List<string>();
        var source = new FakeBitLockerWmi(failure: new PlatformException(PlatformErrorCode.AccessDenied, "not admin"));

        var ex = Should.Throw<PlatformException>(() => new BitLockerQuery(source, warnings.Add).GetStates([Volume1]));

        ex.Code.ShouldBe(PlatformErrorCode.AccessDenied);
        warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TwoRowsForOneVolumeAreUnknown()
    {
        var source = new FakeBitLockerWmi(
        [
            new BitLockerVolumeRecord(Volume1, 1, 0),
            new BitLockerVolumeRecord(Volume1.TrimEnd('\\'), 0, 0),
        ]);

        var states = new BitLockerQuery(source).GetStates([Volume1]);

        states[Key(Volume1)].ShouldBe(EncryptionState.Unknown);
    }

    [Fact]
    public void RowsWithoutADeviceIdAreIgnored()
    {
        var source = new FakeBitLockerWmi([new BitLockerVolumeRecord(" ", 1, 0)]);

        var states = new BitLockerQuery(source).GetStates([Volume1]);

        states[Key(Volume1)].ShouldBe(EncryptionState.Unknown);
    }

    [Fact]
    public void EachRequestedVolumeIsInTheResultOnce()
    {
        var states = new BitLockerQuery(new FakeBitLockerWmi([])).GetStates([Volume1, Volume1.ToUpperInvariant()]);

        states.Count.ShouldBe(1);
    }

    private static uint? ToNullable(int value) => value < 0 ? null : (uint)value;

    private static string Key(string volumePath) => VolumePath.Normalize(volumePath);

    private static string Show(uint? value) =>
        value is null ? "null" : value.Value.ToString(CultureInfo.InvariantCulture);
}
