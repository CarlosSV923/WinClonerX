# Contrato: Puertos de la aplicación (arquitectura hexagonal)

Interfaces que la capa `WinCopy.Application` define y que `WinCopy.Infrastructure.Windows` implementa.
Los casos de uso solo dependen de estas firmas. En las pruebas se sustituyen por fakes, que pueden
inyectar fallos. Las firmas son orientativas. La implementación puede ajustar detalles, pero no la
semántica.

Convenciones:
- Todas las operaciones de E/S son asíncronas y aceptan `CancellationToken`.
- Los errores de plataforma se traducen a `PlatformException(PlatformErrorCode, string technicalDetail)`.
  El código es estable y localizable, y el detalle técnico solo va al log.
- **Ningún puerto de escritura acepta el disco del sistema.** Además de la invariante de dominio, los
  adaptadores comprueban de nuevo `IsSystem` antes de abrir un manejador de escritura (defensa en
  profundidad, principio I).

## Inventario y plataforma

```csharp
interface IDiskInventoryReader
{
    Task<DiskInventory> ReadAsync(CancellationToken ct);
}

interface IPlatformFactsProvider
{
    Task<PlatformFacts> GetAsync(DiskInventory inventory, CancellationToken ct);
    // PlatformFacts es un objeto de valor del DOMINIO (WinCopy.Domain.Planning), para que el dominio no
    // dependa de la capa de aplicación. Contiene BitLocker por volumen, batería, VSS disponible y espacio
    // de diferencias, el mínimo de reducción soportado (MSFT_Partition.GetSupportedSize) y, para cada
    // posible destino, si está en uso (si falta el dato, se considera en uso).
}

interface IDeviceChangeMonitor   // FR-011, US3 escenario 2
{
    IObservable<DeviceChange> Changes { get; }
}
```

## Origen

```csharp
interface ISourceResizer   // R6. Solo para la partición de Windows del origen
{
    Task<ByteSize> GetMinimumSizeAsync(PartitionGuid partition, CancellationToken ct);
    Task ShrinkAsync(PartitionGuid partition, ByteSize newLength, CancellationToken ct);
    Task RestoreAsync(PartitionGuid partition, ByteSize originalLength, CancellationToken ct);
}

interface ISnapshotService   // R1
{
    Task<ISnapshotSet> CreateAsync(IReadOnlyList<PartitionGuid> ntfsVolumes, CancellationToken ct);
}

interface ISnapshotSet : IAsyncDisposable   // Dispose = borrar las instantáneas (siempre)
{
    IBlockSource OpenVolume(PartitionGuid partition);
}

interface IBlockSource : IDisposable
{
    ByteSize Length { get; }
    Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct);   // R2: tramos ocupados + región final
    ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct);
}

interface IRawPartitionReader   // EFI, MSR y particiones no NTFS (copia en bruto)
{
    IBlockSource Open(DiskId source, Extent extent);
}
```

## Destino

```csharp
interface ITargetDisk : IAsyncDisposable   // R4. Se obtiene solo con un ClonePlan Confirmed
{
    Task LockAndTakeOfflineAsync(CancellationToken ct);            // falla si está en uso
    Task WipeBoundariesAsync(CancellationToken ct);                // primeros y últimos MiB
    ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct);
    ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct); // verificación
    Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct);  // MBR protector + GPT (compromiso)
    Task BringOnlineAsync(CancellationToken ct);                   // + IOCTL_DISK_UPDATE_PROPERTIES
    Task DeleteLayoutAsync(CancellationToken ct);                  // compensación
}

interface ITargetDiskFactory
{
    ITargetDisk Open(ClonePlan confirmedPlan);   // lanza una excepción si el plan no está Confirmed o el destino es IsSystem
}

interface IFileSystemExpander   // FSCTL_EXTEND_VOLUME
{
    Task ExpandToPartitionAsync(PartitionGuid targetPartition, CancellationToken ct);
}

interface ITemporaryMounter
{
    Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct);
}

sealed record TemporaryMount(string Root, IAsyncDisposable Release);   // Release = retirar el punto de montaje
```

## Arranque del clon

```csharp
interface IOfflineRegistryEditor   // R5
{
    Task RewriteMountedDevicesAsync(string windowsRoot, MountedDevicesRewrite rewrite, CancellationToken ct);
}

interface IBootConfigurator   // R7: bcdboot <W>\Windows /s <S> /f UEFI
{
    Task RebuildAsync(string windowsRoot, string espRoot, CancellationToken ct);
}

interface IRecoveryConfigurator   // R7: reagentc /setreimage ... /target ...
{
    Task<RecoveryConfigResult> ConfigureAsync(string windowsRoot, string recoveryRoot, CancellationToken ct);
}

interface IStructuralVerifier
{
    Task<VerificationResult> VerifyAsync(ClonePlan plan, CancellationToken ct);
}
```

## Transversales

```csharp
interface IPowerManager   // R10
{
    IDisposable PreventSleep();
    Task<PowerStatus> GetStatusAsync(CancellationToken ct);
}

interface IExecutionLog   // R14, contrato en log-events.md
{
    void Record(LogEvent e);
    string CurrentFile { get; }
    Task ExportAsync(string destinationPath, bool includeInventory, CancellationToken ct);
}

interface IClock { DateTimeOffset UtcNow { get; } }
```

## Casos de uso (puertos de entrada)

| Caso de uso | Entrada → salida | Requisitos |
|-------------|------------------|------------|
| `GetInventoryAndEligibility` | — → `DiskInventory`, `PreflightReport`, `TargetEligibility[]` | FR-001…006 |
| `BuildClonePlan` | `DiskId target`, `VerificationMode` → `ClonePlan (Draft)` o rechazo | FR-007…010 |
| `ConfirmClonePlan` | `PlanId`, avisos aceptados, `TargetConfirmation`, `ShrinkConfirmation?` → `ClonePlan (Confirmed)` | FR-012…014 |
| `ExecuteClone` | `PlanId` (Confirmed) → flujo de `ExecutionEvent` + `CloneResultReport` | FR-011, 015…024 |
| `CancelExecution` | `ExecutionId` → aceptado o en espera (fase no cancelable) | US3 |
| `ExportLog` | ruta e inventario sí/no → archivo | FR-026 |
