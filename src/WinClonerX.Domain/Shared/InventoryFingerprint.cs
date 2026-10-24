using System.Security.Cryptography;
using System.Text;

namespace WinClonerX.Domain.Shared;

/// <summary>Hash SHA-256 en hexadecimal de la identidad y el diseño de los discos de origen y destino.</summary>
public readonly record struct InventoryFingerprint(string Value)
{
    /// <summary>Valor hexadecimal de la huella; no puede estar vacío.</summary>
    public string Value { get; } = string.IsNullOrWhiteSpace(Value)
        ? throw new ArgumentException("La huella no puede estar vacía.", nameof(Value))
        : Value;

    /// <summary>Calcula la huella como SHA-256 del UTF-8 de las partes unidas con '\n'.</summary>
    public static InventoryFingerprint Compute(IEnumerable<string> canonicalParts)
    {
        ArgumentNullException.ThrowIfNull(canonicalParts);

        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', canonicalParts));
        return new InventoryFingerprint(Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
