# Contrato: Eventos del registro de operación

Formato: JSON compacto de Serilog (CLEF), una línea por evento, en
`%ProgramData%\WinCopy\logs\winCopy-<yyyyMMdd-HHmmss>-<executionId>.clef`. El registro no contiene datos
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
| `PhaseStarted` / `PhaseCompleted` | `Phase`, `DurationMs` (al completar) | En cada fase |
| `DiskOperation` | `Disk`, `Operation` (Read/Write/Lock/Offline/Online/CommitLayout/DeleteLayout/Shrink/Restore/Extend), `Offset?`, `Length?`, `DurationMs`, `Result` | Operaciones de disco (las lecturas y escrituras se agregan por tramo, no por búfer) |
| `ProgressReported` | `BytesDone`, `BytesTotal`, `ThroughputBps`, `EtaSeconds?` | Como máximo 1 cada 5 s |
| `ExternalToolRun` | `Tool` (bcdboot/reagentc), `Arguments`, `ExitCode`, `StdOut`, `StdErr` | Herramientas del sistema |
| `CompensationCompleted` | `Compensation`, `Result` (`Succeeded`/`Failed`) | En cancelación o fallo |
| `CleanupFailed` (Warning) | `Step`, `TechnicalDetail` | Un paso de la limpieza (soltar montajes, borrar la instantánea, cerrar el destino) falló después de una verificación correcta. El clon sigue siendo válido y el resultado es `Completed` |
| `ExecutionCompleted` / `ExecutionCancelled` / `ExecutionFailed` | `Outcome`, `FailureCode?`, `Phase?`, `TechnicalDetail?`, `DurationMs` | Al terminar |

## Exportación

La exportación produce un `.zip` con el archivo `.clef` de la ejecución y, si se pide, `inventory.json`.
No incluye nada más del sistema.
