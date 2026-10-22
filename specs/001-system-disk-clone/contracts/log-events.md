# Contrato: Eventos del registro de operación

Formato: JSON compacto de Serilog (CLEF), una línea por evento, en
`%ProgramData%\WinClonerX\logs\winClonerX-<yyyyMMdd-HHmmss>-<executionId>.clef`. El registro no contiene datos
personales más allá de los identificadores de disco (modelo, número de serie y GUID) necesarios para el
diagnóstico. Nunca sale del equipo salvo que la persona lo exporte (FR-026, FR-027).

## Propiedades comunes

| Propiedad | Tipo | Descripción |
|-----------|------|-------------|
| `@t` | ISO-8601 UTC | Marca de tiempo |
| `@mt` | string | Plantilla del mensaje, en inglés |
| `@l` | string | Nivel (omitido = Information) |
| `ExecutionId` | GUID | Igual en todos los eventos de una ejecución |
| `Event` | string | Nombre del evento (tabla siguiente) |
| `AppVersion` | string | |

## Eventos

| `Event` | Propiedades adicionales | Cuándo |
|---------|-------------------------|--------|
| `InventoryCaptured` | `Firmware`, `Disks[]` (id, modelo, serie, tamaño, bus, particiones) | Al leer el inventario |
| `PreflightEvaluated` | `Checks[]` (code, severity, facts) | Al evaluar las comprobaciones previas |
| `PlanCreated` | `PlanId`, `Source`, `Target`, `Mappings[]`, `SourceShrink?`, `Verification`, `EstimateBytes` | Al calcular el plan |
| `PlanConfirmed` | `PlanId`, `AcceptedWarnings[]`, `ShrinkConfirmed` | Al confirmar |
| `PlanInvalidated` (Warning) | `PlanId`, `Reason` (p. ej. `DisksChanged`) | Cuando la huella del inventario cambió entre el plan y la confirmación o la ejecución (FR-011) |
| `ExecutionStarted` | `PlanId` | |
| `CancelRequested` | `Phase`, `CancelPending` | Una vez cuando se acepta una solicitud de cancelación; indica si queda pendiente hasta terminar una fase no interrumpible |
| `PhaseStarted` / `PhaseCompleted` | `Phase`, `DurationMs` (al completar) | En cada fase |
| `DiskOperation` | `Disk`, `Operation` (Read/Write/Lock/Offline/Online/CommitLayout/DeleteLayout/Shrink/Restore/Extend), `Offset?`, `Length?`, `DurationMs`, `Result` | Operaciones de disco (las lecturas y escrituras se agregan en un evento por partición copiada, no por tramo ni por búfer) |
| `ProgressReported` | `BytesDone`, `BytesTotal`, `ThroughputBps`, `EtaSeconds?` | Como máximo 1 cada 5 s |
| `ExternalToolRun` | `Tool` (bcdboot/reagentc), `Arguments`, `ExitCode`, `StdOut`, `StdErr` | Herramientas del sistema |
| `CompensationCompleted` | `Compensation`, `Result` (`Succeeded`/`Failed`) | En cancelación o fallo |
| `CleanupFailed` (Warning) | `Step`, `TechnicalDetail` | Un paso de la limpieza (soltar montajes, borrar la instantánea, cerrar el destino) falló después de una verificación correcta. El clon sigue siendo válido y el resultado es `Completed` |
| `SourceRestoreFailed` (Warning) | `Partition`, `OriginalLength`, `TechnicalDetail` | La restauración del tamaño original del origen falló después de un clon verificado. El clon sigue siendo válido: el resultado es `Completed` con `SourceRestored = false`, y la partición del origen queda reducida |
| `DeviceMonitorUnavailable` (Warning) | `TechnicalDetail` | No se pudo activar la vigilancia de la desconexión de los discos. El clonado sigue: una desconexión se detecta igualmente por el error de E/S que provoca |
| `SourceRestoreJournalWritten` | `Partition`, `OriginalLength`, `PlanId` | Antes de reducir el origen: el diario de restauración tiene la entrada (FR-021a). Sin esta entrada no hay reducción (`ExecutionFailed` con `ShrinkJournalFailed`) |
| `SourceRestoreJournalCleared` | `Partition` | El origen tiene su longitud original y el diario se ha borrado (camino feliz o compensación) |
| `SourceRestoreJournalClearFailed` (Warning) | `Partition`, `TechnicalDetail` | No se ha podido borrar el diario. Es un aviso: el origen ya tiene su longitud original |
| `PendingSourceRestoreFound` (Warning) | `Partition?`, `OriginalLength?`, `PlanId?`, `RecordedAt?`, `TechnicalDetail?` | Al iniciarse hay una entrada pendiente del diario. Si el archivo está dañado, los campos de la entrada son nulos y `TechnicalDetail` lo explica |
| `PendingSourceRestoreCompleted` | `Partition`, `OriginalLength`, `Result` (`Restored`/`AlreadyRestored`) | La recuperación al iniciarse ha resuelto la entrada pendiente |
| `PendingSourceRestoreFailed` (Warning) | `Partition?`, `OriginalLength?`, `Reason` (código de `PendingSourceRestoreIssue`), `TechnicalDetail?` | La recuperación al iniciarse no ha restaurado el origen y necesita atención. No toca la partición y conserva el diario |
| `FullVerificationCompleted` (Error si no pasa) | `Passed`, `DifferingBytes`, `FirstDifferenceOffsets[]` (como mucho 10, desplazamientos absolutos en el disco de destino), `Issues[]` (una línea por partición con diferencias) | Al terminar la verificación completa (FR-022a). Solo en los clones con la verificación completa activada |
| `ExecutionCompleted` / `ExecutionCancelled` / `ExecutionFailed` | `Outcome`, `FailureCode?`, `Phase?`, `TechnicalDetail?`, `DurationMs` | Al terminar |

En `DiskOperation` de la copia de datos, `Disk` identifica a quién se lee o escribe:

- `Read`: el origen. Con instantánea, `snapshot of {GUID de la partición}`; con tramo en bruto, `{disco}, extent at {desplazamiento del tramo}`. `Offset` es un desplazamiento **dentro del volumen de origen** (o del tramo), no del disco; es el del primer tramo ocupado, que casi siempre es 0 (el sector 0 siempre se copia).
- `Write`: el disco de destino, con la misma etiqueta que sus eventos `Lock`, `Offline`, `Online` y `CommitLayout`. `Offset` es absoluto en el disco de destino.
- `Read` de la verificación completa: una por partición, sobre el destino (`Disk` es la etiqueta del destino, la misma que
  la de sus `Write`). `Offset` es absoluto en el disco de destino, del primer tramo ocupado, y `Length` son los bytes leídos
  del destino. No hay lecturas del origen en este evento; un fallo de lectura del origen aparece en `ExecutionFailed`.

## Exportación

La exportación produce un `.zip` con el archivo `.clef` de la ejecución y, si se pide, `inventory.json`.
No incluye nada más del sistema.
