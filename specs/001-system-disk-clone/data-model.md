# Data Model: Clonado del disco del sistema

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Research**: [research.md](research.md)

Modelo de dominio (DDD). Todo lo de este documento vive en `WinClonerX.Domain`, que no tiene dependencias
de Windows. Los nombres en `código` forman el lenguaje ubicuo, que se recogerá en `docs/glosario.md`.

## Contextos delimitados

| Contexto | Responsabilidad | Raíz de agregado / servicios |
|----------|-----------------|------------------------------|
| **Inventario** | Representar el estado de los discos en un instante | `DiskInventory` (objeto de valor) |
| **Planificación** | Decidir si se puede clonar y cómo | `ClonePlan` (agregado), `ClonePlanner`, `PreflightEvaluator`, `TargetLayoutCalculator` (servicios de dominio) |
| **Ejecución** | Aplicar un plan confirmado y registrar su progreso | `CloneExecution` (agregado, máquina de estados) |
| **Reparación de arranque** | Describir los ajustes del clon para que arranque | `BootRepairSpec` (objeto de valor generado a partir del plan) |

Los contextos se comunican mediante objetos de valor inmutables (`DiskInventory` → `ClonePlan` →
`CloneExecution`). La interacción con Windows se hace solo a través de los puertos de
[contracts/ports.md](contracts/ports.md).

---

## Objetos de valor compartidos

| Tipo | Definición | Reglas |
|------|------------|--------|
| `ByteSize` | Cantidad de bytes (`long`) | ≥ 0. Operaciones aritméticas y de formato (GiB/GB) |
| `SectorSize` | Tamaño de sector lógico o físico | Vale 512 o 4096 en la v1 |
| `DiskId` | Número de disco del sistema + GUID de disco | El GUID es la identidad. El número es solo para las APIs |
| `PartitionGuid` | GUID único de la partición GPT | No puede estar vacío |
| `GptPartitionType` | GUID del tipo GPT | Constantes conocidas: ESP, MSR, BasicData, Recovery, LDM*, Spaces* |
| `Extent` | (`Offset`, `Length`) en bytes | El desplazamiento está alineado al sector y la longitud es > 0 |
| `InventoryFingerprint` | Hash de la identidad y el diseño de origen y destino | Es igual si y solo si no cambió nada relevante |

---

## Contexto: Inventario

### `DiskInventory` (objeto de valor, raíz)
- `CapturedAt: DateTimeOffset`
- `Firmware: FirmwareType` (`Uefi` | `Bios` | `Unknown`)
- `Disks: IReadOnlyList<Disk>`
- `Fingerprint(DiskId source, DiskId target): InventoryFingerprint`
- **Invariante**: hay exactamente un disco con `IsSystem = true`. Si no hay ninguno o hay más de uno, el
  inventario es *no soportado*.

### `Disk` (entidad dentro del inventario)
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | `DiskId` | |
| `Model` | `string` | |
| `SerialNumber` | `string` | Puede estar vacío en algunos USB. Si lo está, la confirmación usa el GUID (R13) |
| `Size` | `ByteSize` | |
| `LogicalSectorSize`, `PhysicalSectorSize` | `SectorSize` | |
| `Bus` | `BusKind` | `Nvme`, `Sata`, `Sas`, `Usb`, `FileBackedVirtual`, `Spaces`, `Raid`, `Other` |
| `PartitionStyle` | `PartitionStyle` | `Gpt`, `Mbr`, `Raw` |
| `IsSystem` | `bool` | Contiene el Windows en ejecución o la partición EFI en uso |
| `Partitions` | `IReadOnlyList<Partition>` | Ordenadas por desplazamiento |
| `IsOffline`, `IsReadOnly` | `bool` | |

### `Partition` (objeto de valor)
| Campo | Tipo | Notas |
|-------|------|-------|
| `Number` | `int` | |
| `Extent` | `Extent` | |
| `Type` | `GptPartitionType` | |
| `UniqueId` | `PartitionGuid` | |
| `Attributes` | `ulong` | Atributos GPT |
| `Name` | `string` | Nombre GPT |
| `Role` | `PartitionRole` | Derivado: `EfiSystem`, `MsReserved`, `Windows`, `Recovery`, `Data`, `Unsupported` |
| `Volume` | `Volume?` | Ausente en MSR o si no hay sistema de archivos |

### `Volume` (objeto de valor)
- `FileSystem: FileSystemKind` (`Ntfs`, `Fat32`, `ExFat`, `ReFs`, `Raw`, `Other`)
- `Capacity: ByteSize`, `Used: ByteSize`
- `ClusterSize: ByteSize?`
- `Encryption: EncryptionState` (`None`, `BitLockerOn`, `BitLockerSuspended`, `Unknown`)
- `IsWindowsVolume: bool` (contiene el `%SystemRoot%` en ejecución)
- `InUseByOthers: bool?` (solo se evalúa en los candidatos a destino; `null` = no evaluado)

---

## Contexto: Planificación

### `PreflightReport` (objeto de valor)
- `Checks: IReadOnlyList<PreflightCheck>`
- `IsBlocked => Checks.Any(c => c.Severity == Blocking)`

### `PreflightCheck` (objeto de valor)
| Campo | Tipo |
|-------|------|
| `Code` | `PreflightCode`: `FirmwareNotUefi`, `SourceNotGpt`, `UnsupportedDiskKind`, `MultipleWindows`, `TargetIsSystem`, `TargetInUse`, `TargetTooSmall`, `SectorSizeMismatch`, `ShrinkInsufficient`, `SnapshotUnavailable`, `SnapshotSpaceLow`, `BitLockerOn`, `OnBattery`, `TargetNotEmpty` |
| `Severity` | `Pass` / `Warning` (requiere aceptación) / `Blocking` |
| `Facts` | Datos para el mensaje localizado (p. ej. espacio que falta) |

La interfaz traduce `Code` y `Facts` a textos con recursos (`.resx`). El dominio no contiene textos.

### `TargetEligibility` (objeto de valor, uno por disco no del sistema)
- `Disk: DiskId`, `IsEligible: bool`, `Reasons: IReadOnlyList<PreflightCheck>`
- `Strategy: SizeStrategy?` (`SameOrLarger` | `ShrinkSource`)
- `Shortfall: ByteSize?` (cuánto falta, si no cabe)

### `ClonePlan` (agregado raíz)
| Campo | Tipo | Notas |
|-------|------|-------|
| `Id` | `PlanId` | |
| `Source`, `Target` | `DiskId` | |
| `Fingerprint` | `InventoryFingerprint` | Del inventario con el que se calculó el plan |
| `Mappings` | `IReadOnlyList<PartitionMapping>` | Una por partición de origen (disco completo, FR-021b) |
| `SourceShrink` | `SourceShrinkStep?` | Solo con la estrategia `ShrinkSource` |
| `NewDiskGuid` | `Guid` | Generado (FR-017) |
| `Verification` | `VerificationMode` | `Structural` (siempre) o `StructuralAndFull` (opcional, FR-022a) |
| `Warnings` | `IReadOnlyList<PreflightCheck>` | Avisos que requieren aceptación |
| `Estimate` | `CloneEstimate` | Bytes a copiar y duración estimada |
| `Status` | `PlanStatus` | `Draft` → `Confirmed` (o `Invalidated`) |

**Comportamiento**:
- `ClonePlan.Create(...)` solo lo invoca `ClonePlanner`. Valida todas las invariantes.
- `AcceptWarnings(IEnumerable<PreflightCode>)`: debe cubrir todos los avisos del plan.
- `Confirm(TargetConfirmation, ShrinkConfirmation?)`: pasa a `Confirmed` solo si:
  - todos los avisos están aceptados;
  - `TargetConfirmation` coincide con el `Target` (los 4 últimos caracteres del número de serie, o del
    GUID si no hay número de serie, R13);
  - y, si hay `SourceShrink`, existe una `ShrinkConfirmation` explícita (FR-013).
- `Invalidate(reason)`: cuando la huella cambia (FR-011).

**Invariantes** (cada una se prueba con un test unitario):
1. `Target ≠ Source` y el destino no es `IsSystem` (FR-004, principio I).
2. `Target.LogicalSectorSize == Source.LogicalSectorSize`.
3. Las extensiones de destino no se solapan, están alineadas a 1 MiB, quedan dentro del área utilizable
   de la GPT y siguen el orden del origen.
4. Solo la correspondencia con `Role == Windows` puede tener `TargetLength ≠ SourceLength` (FR-021b).
5. Si el destino es más pequeño que el área usada del origen, `SourceShrink` existe y el
   `NewWindowsLength` es ≥ lo ocupado + el margen (R6).
6. Lo ocupado + el margen ≤ la capacidad de destino asignada (FR-010).
7. Los GUID nuevos (disco y particiones) son únicos y distintos de los del origen.

### `PartitionMapping` (objeto de valor)
| Campo | Tipo | Notas |
|-------|------|-------|
| `Source` | `Partition` | |
| `TargetExtent` | `Extent` | |
| `NewUniqueId` | `PartitionGuid` | |
| `CopyMethod` | `UsedClustersFromSnapshot` (NTFS) / `RawFull` (EFI, MSR, otros) | |
| `Resize` | `None` / `GrowFileSystem` | Solo la partición de Windows con destino más grande |

### `SourceShrinkStep` (objeto de valor)
- `Partition: PartitionGuid`, `OriginalLength: ByteSize`, `ShrunkLength: ByteSize`,
  `MinimumSupported: ByteSize`
- La restauración a `OriginalLength` es un paso de compensación obligatorio (FR-021a).

### `TargetConfirmation` / `ShrinkConfirmation` (objetos de valor)
- `TargetConfirmation(DiskId disk, string typedSerialSuffix, DateTimeOffset at)`
- `ShrinkConfirmation(PartitionGuid partition, ByteSize shrunkLength, DateTimeOffset at)`

### Servicios de dominio
- **`PreflightEvaluator`**: `(DiskInventory, PlatformFacts) → PreflightReport` y
  `TargetEligibility[]`. `PlatformFacts` recoge BitLocker, batería, disponibilidad de VSS y el mínimo de
  reducción, que la capa de aplicación obtiene de los puertos.
- **`TargetLayoutCalculator`**: `(source partitions, target disk, strategy, margin) →
  IReadOnlyList<Extent>`. Es una función pura y la parte más probada.
- **`ClonePlanner`**: orquesta los dos servicios anteriores, genera los GUID nuevos y construye el
  `ClonePlan`.
- **`CapacityPolicy`**: margen = máx(10 % de lo ocupado, 10 GiB). Es configurable para las pruebas.

---

## Contexto: Ejecución

### `CloneExecution` (agregado raíz)
| Campo | Tipo |
|-------|------|
| `Id` | `ExecutionId` |
| `Plan` | `ClonePlan` (debe estar `Confirmed`) |
| `State` | `ExecutionState` |
| `Phase` | `ExecutionPhase` |
| `Progress` | `CloneProgress` (`BytesDone`, `BytesTotal`, `Throughput`, `Eta?`) |
| `Failure` | `ExecutionFailure?` (`Code`, `Phase`, detalle técnico) |
| `Compensations` | Resultado de cada compensación (restauración del origen, limpieza del destino) |
| `Result` | `CloneResultReport?` |

**Fases** (en orden; `ShrinkSource` y `FullVerification` son condicionales):

```text
Revalidate → ShrinkSource? → Snapshot → PrepareTarget → CopyData → FullVerification?
→ CommitLayout → ExpandFileSystem? → RepairBoot → StructuralVerification
→ Cleanup (borrar instantánea, desmontar temporales) → RestoreSource? → Done
```

**Estados y transiciones**:

```text
Pending ──Start──▶ Running ──(todas las fases OK)──▶ Completed
                     │  ├──Cancel solicitado──▶ Cancelling ──▶ Cancelled
                     │  └──error en fase──▶ Failing ──▶ Failed
```

- `Cancelling` y `Failing` ejecutan las **compensaciones** en este orden:
  1. Si se pasó `CommitLayout`, borrar el diseño del destino (para que no se pueda arrancar de él).
  2. Borrar la instantánea.
  3. Retirar los montajes temporales.
  4. Restaurar el tamaño del origen si se redujo.
  5. Permitir de nuevo la suspensión.
- El fallo de una compensación se registra y se muestra de forma destacada (FR-021a). No oculta el error
  original.
- La cancelación solo se acepta en las fases que la admiten. Durante `CommitLayout` (que dura
  milisegundos) se espera a que termine la fase.
- **Invariante**: el disco de origen solo se modifica en `ShrinkSource` y `RestoreSource` (FR-021).
- **Eventos de dominio**: `ExecutionStarted`, `PhaseStarted`, `PhaseCompleted`, `ProgressReported`,
  `CompensationCompleted`, `ExecutionCompleted`, `ExecutionCancelled`, `ExecutionFailed`. Se registran
  en el log (principio VII) y alimentan la interfaz.

### `CloneResultReport` (objeto de valor)
- `Outcome: Completed | Cancelled | Failed`
- `Structural: VerificationResult` (tabla de particiones = plan, volúmenes legibles, BCD presente)
- `Full: VerificationResult?` (chunks y bytes distintos, primeros diez offsets absolutos y resumen por partición en bytes y chunks)
- `SourceRestored: bool?`, `RecoveryEnvironment: Configured | Warning`
- `Duration`, `LogFile: string`, `NextSteps: IReadOnlyList<NextStepCode>`

---

## Contexto: Reparación de arranque

### `BootRepairSpec` (objeto de valor, derivado de `ClonePlan`)
- `WindowsPartition: PartitionGuid` (el nuevo), `EspPartition: PartitionGuid`,
  `RecoveryPartition: PartitionGuid?`
- `MountedDevicesRewrite`: `\DosDevices\C:` → `DMIO:ID:` + nuevo GUID; se eliminan las entradas con GUID
  antiguos (R5)
- `Steps`: `RewriteMountedDevices`, `RebuildBcd`, `ReconfigureWinRe`


En StructuralAndFull, la copia captura un digest SHA-256 por chunk escrito. FullVerification relee únicamente el destino y compara cada chunk con esos digests; no vuelve a leer el origen.
