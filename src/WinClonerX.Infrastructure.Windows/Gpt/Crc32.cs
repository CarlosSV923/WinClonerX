namespace WinClonerX.Infrastructure.Windows.Gpt;

/// <summary>CRC-32 as GPT uses it: IEEE 802.3, reflected, polynomial 0xEDB88320, initial value and final XOR 0xFFFFFFFF.</summary>
internal static class Crc32
{
    private const uint Polynomial = 0xEDB88320u;
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFFu] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1u) != 0 ? Polynomial ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
