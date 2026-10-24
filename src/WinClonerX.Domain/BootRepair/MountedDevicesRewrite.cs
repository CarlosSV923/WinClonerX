using System.Text;

namespace WinClonerX.Domain.BootRepair;

/// <summary>
/// Cambio en <c>MountedDevices</c> del clon (R5): la letra <see cref="DriveLetter"/> pasa a apuntar al volumen de Windows nuevo
/// (<see cref="NewValue"/>) y se eliminan los valores que apuntan a particiones del origen (<see cref="ValuesToRemove"/>).
/// Cada valor tiene 24 bytes: el prefijo ASCII "DMIO:ID:" seguido del GUID de la partición en formato binario.
/// </summary>
public sealed record MountedDevicesRewrite(char DriveLetter, byte[] NewValue, IReadOnlyList<byte[]> ValuesToRemove)
{
    /// <summary>Longitud en bytes de un valor de MountedDevices en formato GPT.</summary>
    public const int GptValueLength = 24;

    /// <summary>Prefijo ASCII de un valor de MountedDevices en formato GPT.</summary>
    private static ReadOnlySpan<byte> GptPrefix => "DMIO:ID:"u8;

    /// <summary>Valor de MountedDevices (24 bytes) que apunta a la partición con el GUID indicado.</summary>
    public static byte[] GptValue(Guid partitionGuid)
    {
        var value = new byte[GptValueLength];
        GptPrefix.CopyTo(value);
        partitionGuid.ToByteArray().CopyTo(value, GptPrefix.Length);

        return value;
    }

    /// <summary>Indica si el valor apunta a una de las particiones del origen y, por tanto, debe eliminarse.</summary>
    public bool IsStale(ReadOnlySpan<byte> value)
    {
        foreach (var stale in ValuesToRemove)
        {
            if (value.SequenceEqual(stale))
            {
                return true;
            }
        }

        return false;
    }
}
