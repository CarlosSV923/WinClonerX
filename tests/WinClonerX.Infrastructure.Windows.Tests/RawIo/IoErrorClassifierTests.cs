using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

public sealed class IoErrorClassifierTests
{
    // Errores de la familia física: el disco puede tener problemas, pero sigue conectado.
    [Theory]
    [InlineData(23)]   // ERROR_CRC
    [InlineData(27)]   // ERROR_SECTOR_NOT_FOUND
    [InlineData(30)]   // ERROR_READ_FAULT
    [InlineData(1117)] // ERROR_IO_DEVICE
    [InlineData(121)]  // ERROR_SEM_TIMEOUT
    public void PhysicalReadErrorsAreSourceReadErrors(int nativeError)
    {
        var error = new Win32Exception(nativeError);

        IoErrorClassifier.ForSourceRead(error).ShouldBe(PlatformErrorCode.SourceReadError);
        IoErrorClassifier.IsDeviceGone(error).ShouldBeFalse();
    }

    [Theory]
    [InlineData(29)]   // ERROR_WRITE_FAULT
    [InlineData(23)]   // ERROR_CRC
    [InlineData(1117)] // ERROR_IO_DEVICE
    public void PhysicalWriteErrorsAreTargetWriteErrors(int nativeError)
    {
        var error = new Win32Exception(nativeError);

        IoErrorClassifier.ForTargetIo(error).ShouldBe(PlatformErrorCode.TargetWriteError);
        IoErrorClassifier.IsDeviceGone(error).ShouldBeFalse();
    }

    [Theory]
    [InlineData(21)]   // ERROR_NOT_READY
    [InlineData(55)]   // ERROR_DEV_NOT_EXIST
    [InlineData(433)]  // ERROR_NO_SUCH_DEVICE
    [InlineData(1006)] // ERROR_FILE_INVALID: el volumen se ha desmontado debajo de la lectura
    [InlineData(1167)] // ERROR_DEVICE_NOT_CONNECTED
    public void DeviceGoneErrorsOnTheSourceAreSourceDeviceRemoved(int nativeError)
    {
        var error = new Win32Exception(nativeError);

        IoErrorClassifier.IsDeviceGone(error).ShouldBeTrue();
        IoErrorClassifier.ForSourceRead(error).ShouldBe(PlatformErrorCode.SourceDeviceRemoved);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(55)]
    [InlineData(433)]
    [InlineData(1006)]
    [InlineData(1167)]
    public void DeviceGoneErrorsOnTheTargetAreTargetDeviceRemoved(int nativeError)
    {
        var error = new Win32Exception(nativeError);

        IoErrorClassifier.ForTargetIo(error).ShouldBe(PlatformErrorCode.TargetDeviceRemoved);
    }

    [Theory]
    [InlineData(5)]  // ERROR_ACCESS_DENIED
    [InlineData(2)]  // ERROR_FILE_NOT_FOUND: en una lectura, no es una desconexión
    [InlineData(87)] // ERROR_INVALID_PARAMETER
    public void OtherErrorsKeepTheCodeTheyHadBeforeTheClassification(int nativeError)
    {
        var error = new Win32Exception(nativeError);

        IoErrorClassifier.ForSourceRead(error).ShouldBe(PlatformErrorCode.SourceReadError);
        IoErrorClassifier.ForTargetIo(error).ShouldBe(PlatformErrorCode.TargetWriteError);
        IoErrorClassifier.IsDeviceGone(error).ShouldBeFalse();
    }

    [Fact]
    public void ExceptionsWithoutAWin32CodeKeepTheDefaultCodes()
    {
        var error = new IOException("short write");

        IoErrorClassifier.ForSourceRead(error).ShouldBe(PlatformErrorCode.SourceReadError);
        IoErrorClassifier.ForTargetIo(error).ShouldBe(PlatformErrorCode.TargetWriteError);
        IoErrorClassifier.IsDeviceGone(error).ShouldBeFalse();
    }
}
