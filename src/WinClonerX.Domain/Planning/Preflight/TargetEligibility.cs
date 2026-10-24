using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Planning;

/// <summary>
/// Elegibilidad de un disco distinto del sistema como destino. Reasons solo recoge las causas propias del destino
/// que no son "Pass"; IsEligible además exige que el informe global no esté bloqueado.
/// </summary>
public sealed record TargetEligibility(
    DiskId Disk,
    bool IsEligible,
    IReadOnlyList<PreflightCheck> Reasons,
    SizeStrategy? Strategy,
    ByteSize? Shortfall);
