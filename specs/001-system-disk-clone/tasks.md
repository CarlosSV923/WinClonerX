---

description: "Lista de tareas para implementar el clonado del disco del sistema"
---

# Tasks: Clonado del disco del sistema

**Input**: Design documents from `/specs/001-system-disk-clone/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: OBLIGATORIOS. La constitución v1.1.1 (principio IV) exige pruebas primero en el dominio y los
casos de uso, e integración solo sobre VHDX. En cada historia, las tareas de prueba van antes que las de
implementación: deben escribirse y **fallar** antes de implementar.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Estructura de [plan.md](plan.md): `src/WinCopy.*`, `tests/WinCopy.*`, `installer/WinCopy.Installer`, `docs/`.
Abreviaturas usadas en las rutas:
- `Dom` = `src/WinCopy.Domain`
- `App` = `src/WinCopy.Application`
- `Infra` = `src/WinCopy.Infrastructure.Windows`
- `Ui` = `src/WinCopy.App`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Inicializar la solución, los proyectos y la configuración de calidad.

- [X] T001 Crear la solución `WinCopy.slnx` con los proyectos `src/WinCopy.Domain` (classlib net10.0), `src/WinCopy.Application` (classlib net10.0), `src/WinCopy.Infrastructure.Windows` (classlib net10.0-windows) y `src/WinCopy.App` (WPF net10.0-windows), con las referencias Domain ← Application ← Infrastructure.Windows/App (solo hacia dentro)
- [X] T002 Crear los proyectos de pruebas xUnit v3 `tests/WinCopy.Domain.Tests`, `tests/WinCopy.Application.Tests`, `tests/WinCopy.Infrastructure.Windows.Tests`, `tests/WinCopy.Infrastructure.IntegrationTests`, `tests/WinCopy.App.Tests` y `tests/WinCopy.ArchitectureTests`, y añadirlos a `WinCopy.slnx`
- [X] T003 [P] Crear `Directory.Build.props` con `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `ImplicitUsings=enable`, `LangVersion=latest` y `PlatformTarget=x64`
- [X] T004 [P] Crear `Directory.Packages.props` (gestión central) con CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, System.Management, Serilog, Serilog.Sinks.File, Serilog.Formatting.Compact, xunit.v3, Shouldly y NetArchTest.Rules (MIT, justificada en research.md R15)
- [X] T005 [P] Crear `.editorconfig` (convenciones C# y estilo de nombres) y `.gitattributes` (`* text=auto eol=crlf` para `.cs`, `.xaml` y `.md`; `binary` para `.vhdx`) para eliminar las advertencias LF/CRLF
- [X] T006 [P] Crear el proyecto WiX v6 vacío `installer/WinCopy.Installer/WinCopy.Installer.wixproj` (SDK `WixToolset.Sdk`), con `Package.wxs` mínimo (x64, por máquina, `Program Files\WinCopy`) y añadirlo a la solución
- [X] T007 [P] Crear el flujo de CI `.github/workflows/ci.yml` (windows-latest): restaurar, compilar en Release y ejecutar las pruebas no marcadas con el rasgo `Integration`
- [X] T008 [P] Crear `docs/glosario.md` con el lenguaje ubicuo de [data-model.md](data-model.md) (Disco, Partición, Volumen, Inventario, Plan de clonado, Correspondencia de partición, Reducción del origen, Ejecución, Compensación, Compromiso del diseño, Huella del inventario)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Modelo compartido, puertos, protección de escritura en disco, interop base, registro, discos
virtuales de prueba, spikes S1 y S3 y estructura de la interfaz. Ninguna historia puede empezar antes.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Modelo compartido del dominio

- [X] T009 [P] Tests de los objetos de valor en `tests/WinCopy.Domain.Tests/Shared/ValueObjectTests.cs`: `ByteSize` (≥ 0, aritmética, formato GiB/GB), `SectorSize` ("Vale 512 o 4096 en la v1"), `Extent` ("El desplazamiento está alineado al sector y la longitud es > 0"), `PartitionGuid` ("No puede estar vacío") e `InventoryFingerprint` ("Es igual si y solo si no cambió nada relevante")
- [X] T010 [P] Implementar `ByteSize`, `SectorSize`, `Extent`, `DiskId`, `PartitionGuid`, `GptPartitionType` (constantes ESP, MSR, BasicData, Recovery, LDM*, Spaces*) e `InventoryFingerprint` en `src/WinCopy.Domain/Shared/`
- [X] T011 [P] Tests del inventario en `tests/WinCopy.Domain.Tests/Inventory/DiskInventoryTests.cs`: derivación de `PartitionRole` a partir del tipo GPT y del volumen de Windows, e invariante "hay exactamente un disco con `IsSystem = true`" (si no hay ninguno o hay varios, el inventario es *no soportado*)
- [X] T012 [P] Implementar `DiskInventory`, `Disk`, `Partition`, `Volume` y los enumerados `FirmwareType`, `BusKind`, `PartitionStyle`, `PartitionRole`, `FileSystemKind` y `EncryptionState` en `src/WinCopy.Domain/Inventory/` según [data-model.md](data-model.md)
- [X] T013 [P] Implementar el tipo `Result<T, TError>` y los errores de dominio en `src/WinCopy.Domain/Shared/Result.cs`

### Arquitectura, puertos y transversales

- [X] T014 [P] Tests de arquitectura con NetArchTest.Rules en `tests/WinCopy.ArchitectureTests/LayeringTests.cs`: `WinCopy.Domain` no referencia `System.Management`, `System.Windows`, `Microsoft.Win32`, `System.Runtime.InteropServices` (salvo los atributos) ni ningún otro proyecto; `WinCopy.Application` no referencia Infrastructure ni WPF; ningún proyecto referencia librerías HTTP (FR-027)
- [X] T015 Definir todos los puertos de [contracts/ports.md](contracts/ports.md) en `src/WinCopy.Application/Ports/` (un archivo por interfaz, incluidos `TemporaryMount` y `PlatformFacts`), junto con `PlatformException` y el enumerado `PlatformErrorCode` en `src/WinCopy.Application/Errors/`. Los puertos `ITargetDisk`, `ITargetDiskFactory`, `IStructuralVerifier` e `IOfflineRegistryEditor` se definen en la US1, junto con `ClonePlan`, `TargetLayout`, `VerificationResult` y `MountedDevicesRewrite`
- [X] T016 [P] Implementar fakes configurables de todos los puertos, con inyección de fallos por operación, en `tests/WinCopy.Application.Tests/Fakes/` (`FakeDiskInventoryReader`, `FakeSnapshotService`, `FakeTargetDisk` (registra cada escritura con su desplazamiento), `FakeSourceResizer`, `FakeBootConfigurator`, `FakePowerManager` (registra si la suspensión está bloqueada) y el resto)
- [X] T017 [P] Implementar `IExecutionLog` con Serilog (CLEF, archivo por ejecución en `%ProgramData%\WinCopy\logs\winCopy-<yyyyMMdd-HHmmss>-<executionId>.clef`) en `src/WinCopy.Infrastructure.Windows/Logging/SerilogExecutionLog.cs`, con las propiedades comunes de [contracts/log-events.md](contracts/log-events.md)
- [X] T018 [P] Implementar `DiskOperationRecorder` en `src/WinCopy.Infrastructure.Windows/Logging/DiskOperationRecorder.cs`: ayudante que mide y emite el evento `DiskOperation` (`Disk`, `Operation`, `Offset?`, `Length?`, `DurationMs`, `Result`) agregado por tramo y no por búfer (FR-025). Tests en `tests/WinCopy.Infrastructure.Windows.Tests/Logging/DiskOperationRecorderTests.cs`
- [X] T019 [P] Implementar la interop base en `src/WinCopy.Infrastructure.Windows/Interop/`: `Kernel32.cs` (`CreateFile`, `DeviceIoControl`, `GetFirmwareType`, `SetThreadExecutionState`, `GetSystemPowerStatus`, `SetVolumeMountPoint`/`DeleteVolumeMountPoint`), códigos IOCTL y FSCTL en `IoControlCodes.cs` y estructuras (incluida `IOCTL_STORAGE_QUERY_PROPERTY` para `BusType`) en `IoControlStructs.cs`, y `SafeDeviceHandle` (todo con `LibraryImport`)
- [X] T020 [P] Implementar `AlignedBuffer` y `AlignedBufferPool` (`NativeMemory.AlignedAlloc`, 4 MiB, alineados al sector físico) en `src/WinCopy.Infrastructure.Windows/RawIo/AlignedBuffer.cs`, con tests en `tests/WinCopy.Infrastructure.Windows.Tests/RawIo/AlignedBufferTests.cs`
- [X] T021 Implementar `DiskWriteGuard` en `src/WinCopy.Infrastructure.Windows/RawIo/DiskWriteGuard.cs`, el **único** punto por el que se puede abrir un manejador de disco con `GENERIC_WRITE` (defensa en profundidad, principio I):
  - Aplica la regla de producción: el disco no es `IsSystem` ni contiene el volumen de Windows en ejecución.
  - Aplica además las restricciones globales del proceso registradas con `DiskWriteGuard.AddRestriction(IDiskWriteRestriction)`. Una restricción **solo puede añadir prohibiciones, nunca relajar** la regla de producción.
  - Tests en `tests/WinCopy.Infrastructure.Windows.Tests/RawIo/DiskWriteGuardTests.cs`: disco del sistema rechazado; una restricción añadida no puede permitir lo que la regla de producción prohíbe; con una restricción activa, un disco que no la cumple se rechaza antes de llamar a `CreateFile`.

### Discos virtuales de prueba y spikes S1 y S3 (research.md)

- [X] T022 Implementar la utilidad de pruebas VHDX con `virtdisk.dll` (`CreateVirtualDisk`, `AttachVirtualDisk`, `DetachVirtualDisk`, borrado) en `tests/WinCopy.Infrastructure.IntegrationTests/Fixtures/VirtualDiskFixture.cs`. Crea los VHDX en `%TEMP%\WinCopyTests\` y expone el número de disco físico de cada VHDX adjuntado
- [X] T023 Registrar la protección contra discos físicos para **todo** el proceso de pruebas de integración:
  - Un `[ModuleInitializer]` en `tests/WinCopy.Infrastructure.IntegrationTests/Fixtures/VirtualDisksOnlyRestriction.cs` llama a `DiskWriteGuard.AddRestriction` con una restricción que solo autoriza discos con `BusType == FileBackedVirtual (15)` que el propio `VirtualDiskFixture` haya creado bajo `%TEMP%\WinCopyTests\` y registrado en `VirtualDiskRegistry`. Se aplica a cualquier adaptador, aunque la prueba lo cree directamente sin el fixture (principio IV: "aunque la prueba esté mal configurada").
  - Tests en `tests/WinCopy.Infrastructure.IntegrationTests/Fixtures/PhysicalDiskGuardTests.cs`: `RejectsSystemDisk`; `AdapterCreatedDirectlyOnPhysicalDiskIsRejected` (crea `TargetDiskFactory`/`TargetDisk` sin fixture apuntando al disco 0 y espera la excepción antes de abrir un manejador); `RestrictionIsActiveAtStartup`.
- [X] T024 Spike S1 en `tests/WinCopy.Infrastructure.IntegrationTests/Spikes/OfflineRawWriteSpike.cs`: sobre un VHDX, poner el disco fuera de línea de forma no persistente (`IOCTL_DISK_SET_DISK_ATTRIBUTES`), escribir datos en bruto y una GPT generada a mano, volver a ponerlo en línea, llamar a `IOCTL_DISK_UPDATE_PROPERTIES` y comprobar que Windows reconoce las particiones. Toda escritura pasa por `DiskWriteGuard`. Registrar el resultado (ruta principal o alternativa de R4) en `specs/001-system-disk-clone/research.md`
- [X] T025 Spike S3 en `tests/WinCopy.Infrastructure.IntegrationTests/Spikes/BitLockerSnapshotSpike.cs`, independiente de la interop VSS de la US1:
  - Crear un VHDX NTFS con BitLocker (protector de contraseña).
  - Crear una instantánea con WMI `Win32_ShadowCopy.Create` (`System.Management`).
  - Leer los clústeres ocupados desde el dispositivo de la instantánea, copiarlos a otro VHDX y borrar la instantánea.
  - Comprobar que el destino se monta como NTFS sin cifrar.
  - Registrar el resultado en `specs/001-system-disk-clone/research.md` (R9).

### Estructura de la aplicación de escritorio

- [X] T026 [P] Crear `src/WinCopy.App/app.manifest` con `requireAdministrator`, compatibilidad con Windows 10/11 y `longPathAware`
- [X] T027 [P] Implementar la infraestructura de localización: `src/WinCopy.App/Resources/Strings.resx` (inglés, neutro), `Strings.es.resx` y la extensión de marcado `{loc:Loc Key}` (clase `LocExtension`) en `src/WinCopy.App/Localization/LocExtension.cs`. La cultura sale de `CultureInfo.CurrentUICulture` con inglés como respaldo
- [X] T028 [P] Tests de localización en `tests/WinCopy.App.Tests/Localization/ResourceParityTests.cs` (cada clave existe en inglés y español y no está vacía, SC-008) y `NoLiteralTextInXamlTests.cs` (ningún `Text=`, `Content=` ni `Header=` literal en los `.xaml`, FR-029)
- [X] T029 Implementar `App.xaml(.cs)` con Generic Host + DI (raíz de composición), instancia única con el mutex `Global\WinCopy.SingleInstance` (si ya existe, trae al frente la ventana existente, FR-031) y el tema Fluent base en `src/WinCopy.App/App.xaml`
- [X] T030 Implementar el tema propio (paleta, tipografía, esquinas, botones primario y destructivo) en `src/WinCopy.App/Themes/WinCopyTheme.xaml`, inspirado en la estética de 4DDiG con recursos propios (principio V)
- [X] T031 Implementar la estructura del asistente: `src/WinCopy.App/MainWindow.xaml` con barra lateral de pasos (`src/WinCopy.App/Controls/StepRail.xaml`), área de contenido y barra inferior Atrás/Siguiente, más `src/WinCopy.App/ViewModels/WizardViewModel.cs` con la navegación entre los 5 pasos de [contracts/ui-wizard.md](contracts/ui-wizard.md)

**Checkpoint**: spikes S1 y S3 resueltos, protección de escritura activa en las pruebas, base compilando con `TreatWarningsAsErrors` y tests de arquitectura y localización en verde. Se pueden empezar las historias.

---

## Phase 3: User Story 1 - Clonar el sistema a un disco de igual o mayor tamaño (Priority: P1) 🎯 MVP

**Goal**: Clonar el disco del sistema completo a un destino igual o mayor, con el espacio extra asignado a
Windows, y que el clon arranque.

**Independent Test**: clonar un sistema de referencia a un disco de prueba más grande (VHDX en el nivel 2
de [quickstart.md](quickstart.md); VM o disco físico en los niveles 3 y 4), retirar el original y
comprobar que Windows arranca desde el clon con `C:` ampliado.

### Tests for User Story 1 ⚠️

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T032 [P] [US1] Tests de `PreflightEvaluator` en `tests/WinCopy.Domain.Tests/Planning/PreflightEvaluatorTests.cs`: un código por caso (`FirmwareNotUefi`, `SourceNotGpt`, `UnsupportedDiskKind`, `MultipleWindows`, `TargetIsSystem`, `TargetInUse`, `TargetTooSmall`, `SectorSizeMismatch`, `SnapshotUnavailable`, `SnapshotSpaceLow`, `BitLockerOn` → Warning, `OnBattery` → Warning, `TargetNotEmpty` → Warning) y el disco del sistema nunca es elegible (FR-004)
- [X] T033 [P] [US1] Tests de `TargetLayoutCalculator` en `tests/WinCopy.Domain.Tests/Planning/TargetLayoutCalculatorTests.cs`: destino igual y mayor; orden conservado; alineación a 1 MiB; las particiones posteriores a Windows (Recovery) van al final con su tamaño; solo crece Windows; sin solapes y dentro del área utilizable GPT; disco con partición de datos adicional (FR-021b)
- [X] T034 [P] [US1] Tests de `ClonePlan` en `tests/WinCopy.Domain.Tests/Planning/ClonePlanTests.cs`, uno por invariante de data-model: (1) "`Target ≠ Source` y el destino no es `IsSystem`"; (2) "`Target.LogicalSectorSize == Source.LogicalSectorSize`"; (3) extensiones sin solape, alineadas a 1 MiB y en orden; (4) "Solo la correspondencia con `Role == Windows` puede tener `TargetLength ≠ SourceLength`"; (6) "Lo ocupado + el margen ≤ la capacidad de destino asignada"; (7) "Los GUID nuevos (disco y particiones) son únicos y distintos de los del origen". Más las transiciones `Draft → Confirmed`, que exigen avisos aceptados y `TargetConfirmation` válida, e `Invalidate`
- [X] T035 [P] [US1] Tests de `TargetConfirmation` en `tests/WinCopy.Domain.Tests/Planning/TargetConfirmationTests.cs`: los 4 últimos caracteres del número de serie sin distinguir mayúsculas; si no hay número de serie, los 4 últimos del GUID de disco (R13); disco distinto → rechazo
- [X] T036 [P] [US1] Tests de `CloneExecution` en `tests/WinCopy.Domain.Tests/Execution/CloneExecutionTests.cs`: secuencia de fases `Revalidate → Snapshot → PrepareTarget → CopyData → CommitLayout → ExpandFileSystem? → RepairBoot → StructuralVerification → Cleanup → Done`, transiciones `Pending → Running → Completed`, `Failing → Failed` con las compensaciones en el orden de data-model, eventos de dominio emitidos y la invariante "el disco de origen solo se modifica en `ShrinkSource` y `RestoreSource`"
- [X] T037 [P] [US1] Tests de `BootRepairSpec` en `tests/WinCopy.Domain.Tests/BootRepair/BootRepairSpecTests.cs`: `\DosDevices\C:` → `DMIO:ID:` + nuevo GUID (24 bytes), eliminación de las entradas con GUID antiguos y pasos derivados del plan
- [X] T038 [P] [US1] Tests de los casos de uso `GetInventoryAndEligibility`, `BuildClonePlan` y `ConfirmClonePlan` en `tests/WinCopy.Application.Tests/UseCases/PlanningUseCasesTests.cs` con fakes
- [ ] T039 [P] [US1] Tests del orquestador `ExecuteClone` en `tests/WinCopy.Application.Tests/UseCases/ExecuteCloneTests.cs`: camino feliz con fakes (ninguna escritura en el disco de origen; `CommitLayout` después de la última escritura de datos; la instantánea se elimina siempre); un fallo inyectado en cada fase deja el destino sin diseño y el origen intacto (SC-005); un cambio de huella provoca abortar y replanificar (FR-011); la suspensión está bloqueada durante toda la ejecución y se libera en todos los desenlaces, según `FakePowerManager` (FR-020)
- [X] T040 [P] [US1] Tests del serializador GPT en `tests/WinCopy.Infrastructure.Windows.Tests/Gpt/GptSerializerTests.cs`: MBR protector, cabecera primaria y de respaldo, CRC32 de cabecera y entradas (vectores de la especificación UEFI), sectores de 512 y 4096 bytes y 128 entradas
- [X] T041 [P] [US1] Tests del cálculo de tramos ocupados en `tests/WinCopy.Infrastructure.Windows.Tests/RawIo/VolumeAllocationTests.cs`: fusión de clústeres contiguos, inclusión del sector 0 y de la región final después del último clúster completo (sector de arranque de respaldo de NTFS)
- [ ] T042 [P] [US1] Test de integración del inventario en `tests/WinCopy.Infrastructure.IntegrationTests/Storage/InventoryReaderTests.cs`: un VHDX GPT con ESP, MSR, NTFS y Recovery aparece con sus roles, tamaños, GUID y `BusKind.FileBackedVirtual`
- [ ] T043 [P] [US1] Test de integración de instantánea y lectura en `tests/WinCopy.Infrastructure.IntegrationTests/Snapshots/VssSnapshotTests.cs`: instantánea de un volumen NTFS de VHDX, mapa de clústeres, lectura alineada y borrado de la instantánea al liberar
- [ ] T044 [US1] Test de integración del clonado completo en `tests/WinCopy.Infrastructure.IntegrationTests/Cloning/CloneToLargerTests.cs`: un VHDX origen de 20 GB (ESP, MSR, NTFS con datos de prueba y Recovery) se clona a un VHDX de 30 GB. Comprueba la tabla GPT del plan, los GUID nuevos, NTFS ampliado al tamaño de la partición, los archivos idénticos (hash), que el origen no cambió (hash de sus sectores) y que el registro contiene eventos `DiskOperation` de las escrituras (FR-025)
- [ ] T045 [P] [US1] Test de integración de `StructuralVerifier` en `tests/WinCopy.Infrastructure.IntegrationTests/Verification/StructuralVerifierTests.cs`: con un clon VHDX correcto da éxito; da error si se altera una entrada GPT, si falta `\EFI\Microsoft\Boot\BCD` en la ESP o si un volumen no se puede montar (FR-022, principio I)
- [ ] T046 [P] [US1] Tests de ViewModels en `tests/WinCopy.App.Tests/ViewModels/`: `DisksStepViewModelTests` (el disco del sistema nunca está en los destinos; los no aptos no se pueden seleccionar y muestran su motivo), `ReviewStepViewModelTests` (Siguiente se deshabilita hasta aceptar todos los avisos), `ConfirmStepViewModelTests` ("Clonar ahora" solo con la confirmación válida) y `ProgressStepViewModelTests` (formato del progreso y del tiempo restante); además, `tests/WinCopy.App.Tests/SingleInstanceTests.cs`: un segundo intento con el mutex `Global\WinCopy.SingleInstance` detecta la instancia existente (FR-031)

### Implementation for User Story 1

**Dominio**

- [X] T047 [P] [US1] Implementar `PreflightCheck`, `PreflightCode`, `Severity`, `PreflightReport` y `TargetEligibility` en `src/WinCopy.Domain/Planning/Preflight/`
- [X] T048 [P] [US1] Implementar `CapacityPolicy` ("margen = máx(10 % de lo ocupado, 10 GiB)", configurable) en `src/WinCopy.Domain/Planning/CapacityPolicy.cs`
- [X] T049 [US1] Implementar `PreflightEvaluator` (con `PlatformFacts` como entrada) en `src/WinCopy.Domain/Planning/PreflightEvaluator.cs` (depende de T047 y T048)
- [X] T050 [US1] Implementar `TargetLayoutCalculator` (estrategia `SameOrLarger`) en `src/WinCopy.Domain/Planning/TargetLayoutCalculator.cs`
- [X] T051 [US1] Implementar `ClonePlan`, `PartitionMapping` (`CopyMethod`: `UsedClustersFromSnapshot` | `RawFull`; `Resize`: `None` | `GrowFileSystem`), `VerificationMode`, `CloneEstimate`, `PlanStatus`, `TargetConfirmation` y `ClonePlanner` en `src/WinCopy.Domain/Planning/`
- [X] T052 [US1] Implementar `CloneExecution`, `ExecutionState`, `ExecutionPhase`, `CloneProgress`, `ExecutionFailure`, los eventos de dominio y `CloneResultReport` en `src/WinCopy.Domain/Execution/`
- [X] T053 [P] [US1] Implementar `BootRepairSpec` y `MountedDevicesRewrite` en `src/WinCopy.Domain/BootRepair/`

**Aplicación**

- [X] T054 [US1] Implementar los casos de uso `GetInventoryAndEligibility`, `BuildClonePlan` y `ConfirmClonePlan` en `src/WinCopy.Application/UseCases/Planning/`
- [ ] T055 [US1] Implementar el orquestador `ExecuteClone` en `src/WinCopy.Application/UseCases/Execution/ExecuteClone.cs`: revalidación de la huella, fases, publicación de `ExecutionEvent` (`IObservable`), compensaciones (`DeleteLayout`, borrar la instantánea, retirar los montajes, liberar la suspensión) y registro de cada evento en `IExecutionLog`
- [ ] T056 [US1] Implementar la estimación de progreso (EWMA de 30 s; tiempo restante desde el 1 % o 10 s; estimación inicial por tipo de bus, R17) en `src/WinCopy.Application/UseCases/Execution/ProgressEstimator.cs`

**Infraestructura**

- [ ] T057 [P] [US1] Implementar `StorageInventoryReader` (`MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume` vía `System.Management` en `root\Microsoft\Windows\Storage` + `IOCTL_DISK_GET_DRIVE_LAYOUT_EX` para los GUID y atributos exactos) en `src/WinCopy.Infrastructure.Windows/Storage/StorageInventoryReader.cs`
- [ ] T058 [P] [US1] Implementar `PlatformFactsProvider` (firmware, `Win32_EncryptableVolume`, batería, disponibilidad de VSS y espacio de diferencias, destino en uso con `FSCTL_LOCK_VOLUME` de prueba) en `src/WinCopy.Infrastructure.Windows/Platform/PlatformFactsProvider.cs`
- [X] T059 [US1] Implementar la interop COM de VSS (`[GeneratedComInterface]` de `IVssBackupComponents` e `IVssAsync`, `LibraryImport` de `CreateVssBackupComponentsInternal`) en `src/WinCopy.Infrastructure.Windows/Interop/Vss/`
- [X] T060 [US1] Implementar `VssSnapshotService` y `VssSnapshotSet` (contexto *backup*, un único *snapshot set*, borrado garantizado en `DisposeAsync`) en `src/WinCopy.Infrastructure.Windows/Snapshots/` (depende de T059)
- [X] T061 [US1] Implementar `SnapshotVolumeSource` (`IBlockSource` sobre `\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN`, `FSCTL_GET_VOLUME_BITMAP` + `FSCTL_GET_NTFS_VOLUME_DATA`, tramos ocupados y región final) en `src/WinCopy.Infrastructure.Windows/RawIo/SnapshotVolumeSource.cs`
- [X] T062 [P] [US1] Implementar `RawPartitionReader` (ESP, MSR y particiones no NTFS completas desde `\\.\PhysicalDriveN`, solo lectura) en `src/WinCopy.Infrastructure.Windows/RawIo/RawPartitionReader.cs`
- [X] T063 [P] [US1] Implementar `GptSerializer` (MBR protector + GPT primaria y de respaldo + CRC32) en `src/WinCopy.Infrastructure.Windows/Gpt/GptSerializer.cs`
- [ ] T064 [US1] Implementar `TargetDisk` y `TargetDiskFactory` (bloquear y desmontar volúmenes, fuera de línea no persistente, `WipeBoundariesAsync`, escritura y lectura alineadas, `CommitLayoutAsync`, `BringOnlineAsync` + `IOCTL_DISK_UPDATE_PROPERTIES`, `DeleteLayoutAsync`; abre el disco solo a través de `DiskWriteGuard` y emite `DiskOperation` con `DiskOperationRecorder` en cada operación) en `src/WinCopy.Infrastructure.Windows/RawIo/TargetDisk.cs`, según la ruta decidida en el spike S1 (T024)
- [ ] T065 [US1] Implementar `CopyPipeline` (tubería con `System.Threading.Channels`, 2–4 búferes de 4 MiB en vuelo, progreso por bytes confirmados; cualquier error de E/S aborta; emite `DiskOperation` agregado por tramo con `DiskOperationRecorder`) en `src/WinCopy.Infrastructure.Windows/RawIo/CopyPipeline.cs`
- [X] T066 [P] [US1] Implementar `FileSystemExpander` (`FSCTL_EXTEND_VOLUME`; emite `DiskOperation` con `Operation = Extend`) en `src/WinCopy.Infrastructure.Windows/Storage/FileSystemExpander.cs`
- [X] T067 [P] [US1] Implementar `TemporaryMounter` (letra libre con `SetVolumeMountPoint`, retirada garantizada) en `src/WinCopy.Infrastructure.Windows/Boot/TemporaryMounter.cs`
- [ ] T068 [P] [US1] Implementar `OfflineRegistryEditor` (`SeBackupPrivilege`/`SeRestorePrivilege`, `RegLoadKey` del `SYSTEM` del clon, reescritura de `MountedDevices`, `RegUnLoadKey` garantizado) en `src/WinCopy.Infrastructure.Windows/Boot/OfflineRegistryEditor.cs`, con interop de advapi32 en `Interop/Advapi32.cs`
- [X] T069 [P] [US1] Implementar `ExternalToolRunner` (ejecuta solo herramientas de `%WINDIR%\System32` por ruta absoluta, captura la salida y el código, y registra el evento `ExternalToolRun`) en `src/WinCopy.Infrastructure.Windows/Boot/ExternalToolRunner.cs`
- [X] T070 [US1] Implementar `BcdBootConfigurator` (`bcdboot <W>:\Windows /s <S>: /f UEFI`; **nunca** sin `/s`, para no tocar la NVRAM) y `ReagentcRecoveryConfigurator` (`reagentc /setreimage /path <R>:\Recovery\WindowsRE /target <W>:\Windows`; un fallo produce el aviso `RecoveryEnvironment: Warning`) en `src/WinCopy.Infrastructure.Windows/Boot/` (depende de T069)
- [ ] T071 [US1] Implementar `StructuralVerifier` (la tabla GPT coincide con el plan, cada volumen se monta y se lee, `\EFI\Microsoft\Boot\BCD` existe en la ESP del clon) en `src/WinCopy.Infrastructure.Windows/Verification/StructuralVerifier.cs`
- [X] T072 [P] [US1] Implementar `PowerManager` (`SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` y estado de batería) en `src/WinCopy.Infrastructure.Windows/Platform/PowerManager.cs`
- [ ] T073 [US1] **Punto de control S2** (manual, VM Gen2 de Hyper-V), antes de empezar la interfaz:
  - Crear el prototipo de línea de comandos `tests/WinCopy.Infrastructure.IntegrationTests/Spikes/CloneCliPrototype.cs`, que compone el orquestador T055 con los adaptadores T057–T072.
  - Clonar dentro de la VM, arrancar el clon y comprobar que el volumen de Windows es `C:` y que `reagentc /info` muestra WinRE habilitado.
  - Documentar el procedimiento y el resultado en `docs/spikes/s2-arranque-vm.md` y en `specs/001-system-disk-clone/research.md` (R5 y R7).
  - Si falla, corregir T068 y T070 antes de seguir.
- [ ] T074 [US1] Registrar todos los adaptadores y casos de uso en el contenedor DI en `src/WinCopy.App/Composition/ServiceRegistration.cs`

**Interfaz**

- [ ] T075 [P] [US1] Implementar los controles `DiskCard.xaml` (modelo, número de serie, capacidad, bus, estado Apto/No apto con motivo; `AutomationProperties.Name`/`HelpText`) y `PartitionBar.xaml` (particiones a escala, color por rol, espacio usado) en `src/WinCopy.App/Controls/`
- [ ] T076 [US1] Implementar el paso 1 "Discos" (`src/WinCopy.App/Views/DisksStepView.xaml` + `src/WinCopy.App/ViewModels/DisksStepViewModel.cs`): tarjeta de origen no seleccionable, lista de destinos, estado vacío con botón Actualizar y panel de bloqueo global, según [contracts/ui-wizard.md](contracts/ui-wizard.md)
- [ ] T077 [US1] Implementar el paso 2 "Revisión" (`src/WinCopy.App/Views/ReviewStepView.xaml` + `src/WinCopy.App/ViewModels/ReviewStepViewModel.cs`): barras "Antes" y "Después", resumen, avisos con casilla "Entiendo" obligatoria y la casilla de verificación completa, aún desactivada (se conecta en T108)
- [ ] T078 [US1] Implementar el paso 3 "Confirmación" (`src/WinCopy.App/Views/ConfirmStepView.xaml` + `src/WinCopy.App/ViewModels/ConfirmStepViewModel.cs`): disco a borrar en grande, campo de los 4 últimos caracteres del número de serie y botón destructivo "Clonar ahora"
- [ ] T079 [US1] Implementar el paso 4 "Progreso" (`src/WinCopy.App/Views/ProgressStepView.xaml` + `src/WinCopy.App/ViewModels/ProgressStepViewModel.cs`): lista de fases, barra, "X GB de Y GB", velocidad, tiempo restante y aviso de cambios no incluidos
- [ ] T080 [US1] Implementar el paso 5 "Resultado" en caso de éxito (`src/WinCopy.App/Views/ResultStepView.xaml` + `src/WinCopy.App/ViewModels/ResultStepViewModel.cs`): verificación, duración, estado de WinRE y pasos siguientes (FR-023)
- [ ] T081 [US1] Añadir a `src/WinCopy.App/Resources/Strings.resx` y `Strings.es.resx` todos los textos de los pasos 1–5, los mensajes por `PreflightCode` y `PlatformErrorCode`, y los pasos siguientes

**Instalador**

- [ ] T082 [US1] Completar `installer/WinCopy.Installer/Package.wxs`: publicación *self-contained* win-x64 de `WinCopy.App`, acceso directo en el menú Inicio, actualización mayor (`MajorUpgrade`) y desinstalación sin componentes residuales (FR-032). Añadir el destino `dotnet build installer/WinCopy.Installer -c Release` en la CI

**Checkpoint**: la US1 funciona por sí sola. Los niveles 1 y 2 de quickstart están en verde, y el nivel 3 (VM) arranca el clon.

---

## Phase 4: User Story 2 - Clonar a un disco más pequeño que el origen (Priority: P2)

**Goal**: admitir destinos más pequeños que el origen reduciendo temporalmente la partición de Windows del
origen, con su propia confirmación y restauración automática.

**Independent Test**: un VHDX de origen con pocos datos se clona a un VHDX más pequeño. El clon es válido y
el origen vuelve a su tamaño original. Después, en VM, arrancar los dos discos (SC-010).

### Tests for User Story 2 ⚠️

- [ ] T083 [P] [US2] Tests de `TargetLayoutCalculator` con estrategia `ShrinkSource` en `tests/WinCopy.Domain.Tests/Planning/TargetLayoutCalculatorShrinkTests.cs`: tamaño reducido = máx(mínimo soportado por Windows, ocupado + margen); las demás particiones conservan su tamaño (FR-021b); destino insuficiente → `TargetTooSmall` con `Shortfall`
- [ ] T084 [P] [US2] Tests de la invariante (5) de `ClonePlan` en `tests/WinCopy.Domain.Tests/Planning/ClonePlanShrinkTests.cs`: "Si el destino es más pequeño que el área usada del origen, `SourceShrink` existe y el `NewWindowsLength` es ≥ lo ocupado + el margen". Además, `Confirm` sin `ShrinkConfirmation` se rechaza (FR-013) y `ShrinkInsufficient` cuando el mínimo de Windows supera lo que cabe
- [ ] T085 [P] [US2] Tests de `CloneExecution` con las fases `ShrinkSource` y `RestoreSource` en `tests/WinCopy.Domain.Tests/Execution/CloneExecutionShrinkTests.cs`: `RestoreSource` se ejecuta tras éxito, fallo y cancelación; un fallo de `RestoreSource` se registra como compensación fallida sin ocultar el error original (FR-021a)
- [ ] T086 [P] [US2] Tests del orquestador con reducción en `tests/WinCopy.Application.Tests/UseCases/ExecuteCloneShrinkTests.cs`: la reducción ocurre antes de la instantánea; si Windows no puede reducir lo suficiente, se aborta **antes** de tocar el destino (US2 escenario 2); restauración en todos los desenlaces
- [ ] T087 [US2] Test de integración en `tests/WinCopy.Infrastructure.IntegrationTests/Cloning/CloneToSmallerTests.cs`: un VHDX de origen de 40 GB con 8 GB de datos se clona a un VHDX de 25 GB. El clon es válido, los archivos son idénticos y la partición del origen recupera su tamaño original
- [ ] T088 [P] [US2] Tests de ViewModels en `tests/WinCopy.App.Tests/ViewModels/ShrinkFlowTests.cs`: indicación de estrategia en el paso 1, panel de reducción con casilla propia en el paso 3 y estado de la restauración en el paso 5

### Implementation for User Story 2

- [ ] T089 [US2] Implementar `SizeStrategy.ShrinkSource`, `SourceShrinkStep` ("`OriginalLength`, `ShrunkLength`, `MinimumSupported`") y `ShrinkConfirmation` en `src/WinCopy.Domain/Planning/`, y ampliar `TargetLayoutCalculator`, `PreflightEvaluator` (`ShrinkInsufficient`) y `ClonePlan.Confirm`
- [ ] T090 [US2] Añadir las fases `ShrinkSource` y `RestoreSource` (compensación obligatoria) a `CloneExecution` en `src/WinCopy.Domain/Execution/` y al orquestador `ExecuteClone` en `src/WinCopy.Application/UseCases/Execution/ExecuteClone.cs`
- [ ] T091 [US2] Implementar `StorageSourceResizer` (`MSFT_Partition.GetSupportedSize` y `MSFT_Partition.Resize` vía `System.Management`; nunca sobre una partición distinta de la de Windows del origen; emite `DiskOperation` con `Operation = Shrink`/`Restore`) en `src/WinCopy.Infrastructure.Windows/Storage/StorageSourceResizer.cs`
- [ ] T092 [US2] Ampliar `PlatformFactsProvider` con el mínimo de reducción soportado en `src/WinCopy.Infrastructure.Windows/Platform/PlatformFactsProvider.cs`
- [ ] T093 [US2] Actualizar la interfaz: indicación "Hay que reducir Windows en el disco original" en `src/WinCopy.App/Controls/DiskCard.xaml`, panel de reducción con casilla propia en `src/WinCopy.App/Views/ConfirmStepView.xaml` y estado de restauración (o aviso destacado con instrucciones) en `src/WinCopy.App/Views/ResultStepView.xaml`
- [ ] T094 [US2] Añadir los textos de la reducción y la restauración a `src/WinCopy.App/Resources/Strings.resx` y `Strings.es.resx`

**Checkpoint**: US1 y US2 funcionan cada una por su lado.

---

## Phase 5: User Story 3 - Cancelación y fallos sin poner en riesgo el sistema (Priority: P3)

**Goal**: cancelar o sufrir un fallo en cualquier fase deja el origen intacto, el destino sin inicializar, un
mensaje claro y un registro exportable.

**Independent Test**: cancelaciones y fallos inyectados en cada fase (fakes y VHDX). El origen sigue
intacto, el destino queda sin tabla de particiones y el registro describe lo ocurrido.

### Tests for User Story 3 ⚠️

- [ ] T095 [P] [US3] Tests de cancelación en `tests/WinCopy.Domain.Tests/Execution/CloneExecutionCancelTests.cs`: `Running → Cancelling → Cancelled` con compensaciones; durante `CommitLayout` la cancelación queda pendiente hasta terminar la fase
- [ ] T096 [P] [US3] Tests del caso de uso `CancelExecution` y de la desconexión de disco en `tests/WinCopy.Application.Tests/UseCases/CancelAndDeviceLossTests.cs`: un `DeviceChange` de origen o destino aborta con `FailureCode` específico; un error de lectura del origen aborta con el código "disco de origen con posibles problemas físicos" (US3 escenario 3)
- [ ] T097 [P] [US3] Tests de exportación en `tests/WinCopy.Application.Tests/UseCases/ExportLogTests.cs`: el `.zip` contiene el `.clef` de la ejecución y `inventory.json` solo si se pide
- [ ] T098 [US3] Test de integración en `tests/WinCopy.Infrastructure.IntegrationTests/Cloning/CancelMidCopyTests.cs`: cancelar al 50 % de CopyData deja el VHDX de destino sin tabla de particiones y el origen sin cambios (hash)
- [ ] T099 [P] [US3] Tests de ViewModels en `tests/WinCopy.App.Tests/ViewModels/CancelFlowTests.cs`: Cancelar pide confirmación; cerrar la ventana equivale a cancelar; texto "Terminando paso seguro…" en fase no cancelable; variantes de resultado Cancelado y Fallido

### Implementation for User Story 3

- [ ] T100 [US3] Implementar el estado `Cancelling` y las fases no cancelables en `CloneExecution` (`src/WinCopy.Domain/Execution/CloneExecution.cs`), y el caso de uso `CancelExecution` en `src/WinCopy.Application/UseCases/Execution/CancelExecution.cs`
- [ ] T101 [P] [US3] Implementar `DeviceChangeMonitor` (`WM_DEVICECHANGE` en ventana de mensajes oculta o `CM_Register_Notification`) en `src/WinCopy.Infrastructure.Windows/Platform/DeviceChangeMonitor.cs`, y conectarlo al orquestador
- [ ] T102 [P] [US3] Implementar la clasificación de errores de E/S (lectura de origen, escritura de destino, dispositivo no disponible) en `PlatformErrorCode` dentro de `src/WinCopy.Infrastructure.Windows/RawIo/IoErrorClassifier.cs`
- [ ] T103 [US3] Implementar el caso de uso `ExportLog` en `src/WinCopy.Application/UseCases/ExportLog.cs` y `ExportAsync` (zip con `.clef` e `inventory.json` opcional) en `src/WinCopy.Infrastructure.Windows/Logging/SerilogExecutionLog.cs`
- [ ] T104 [US3] Actualizar la interfaz: botón Cancelar con confirmación y cierre de ventana interceptado en `src/WinCopy.App/Views/ProgressStepView.xaml` y `src/WinCopy.App/MainWindow.xaml.cs`; variantes Cancelado y Fallido con estado del origen y del destino y botón "Exportar registro" (diálogo para guardar) en `src/WinCopy.App/Views/ResultStepView.xaml`
- [ ] T105 [US3] Añadir los textos de cancelación, fallos por `PlatformErrorCode` y exportación a `src/WinCopy.App/Resources/Strings.resx` y `Strings.es.resx`

**Checkpoint**: las tres historias funcionan de forma independiente.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: verificación completa opcional, accesibilidad, distribución, documentación y validación final.

### Verificación completa opcional (FR-022a, SC-009)

- [ ] T106 [P] Tests en `tests/WinCopy.Application.Tests/UseCases/FullVerificationTests.cs` (una diferencia inyectada en el destino se informa con el número de bytes distintos y los primeros desplazamientos) y en `tests/WinCopy.Infrastructure.IntegrationTests/Cloning/FullVerificationTests.cs` (un byte alterado a propósito en un VHDX se detecta)
- [ ] T107 Implementar la fase `FullVerification` (relee los tramos copiados de la instantánea y del destino, compara y produce `VerificationResult`) en `src/WinCopy.Domain/Execution/` y `src/WinCopy.Application/UseCases/Execution/FullVerification.cs`
- [ ] T108 Conectar la casilla "Verificar todos los datos (aprox. duplica la duración)", desactivada por defecto, con `VerificationMode.StructuralAndFull` en `src/WinCopy.App/ViewModels/ReviewStepViewModel.cs` y mostrar el resultado en `src/WinCopy.App/Views/ResultStepView.xaml`

### Calidad, accesibilidad y textos

- [ ] T109 [P] Revisión de accesibilidad con Accessibility Insights de los pasos 1 y 3 (navegación por teclado, nombres accesibles, foco inicial, contraste) y corrección de hallazgos en `src/WinCopy.App/Views/` (FR-030)
- [ ] T110 [P] Revisión editorial de `src/WinCopy.App/Resources/Strings.resx` y `Strings.es.resx` (tono claro y sin jerga; errores con "qué pasó / qué puedes hacer", principio VII)
- [ ] T111 [P] Prueba de rendimiento en `tests/WinCopy.Infrastructure.IntegrationTests/Performance/ProportionalDurationTests.cs`: 10 GB de datos en un VHDX de 20 GB frente a uno de 100 GB; la duración no difiere más de un 20 % (SC-003; misma proporción que en la especificación)
- [ ] T112 [P] Medición de los objetivos de rendimiento del plan en `tests/WinCopy.Infrastructure.IntegrationTests/Performance/ThroughputAndMemoryTests.cs`:
  - Rendimiento de copia ≥ 85 % de la velocidad secuencial medida del VHDX más lento.
  - Memoria máxima del proceso < 300 MB durante la copia.
  - La respuesta de la interfaz (< 100 ms) se comprueba de forma manual en el nivel 4 y se registra en `docs/validacion-manual.md`.

### Distribución y documentación (constitución v1.1.1)

- [ ] T113 Crear `.github/workflows/release.yml`: compila el MSI, calcula SHA-256, genera GitHub Artifact Attestations (`actions/attest-build-provenance`) y publica la versión con el hash en las notas
- [ ] T114 Crear `README.md` en la raíz del repositorio (en español, con resumen en inglés): qué es WinCopy, requisitos (UEFI/GPT, Windows 10 22H2/11 x64), y una sección de instalación en el orden obligatorio de la constitución: (1) superar el aviso de SmartScreen, (2) verificar SHA-256 y la atestación (`gh attestation verify`), (3) compilar desde el código, (4) **solo como último recurso**, desactivar Smart App Control con la advertencia destacada de que puede no ser reversible sin reinstalar Windows
- [ ] T115 [P] Crear `docs/validacion-manual.md` con la plantilla del nivel 4 de quickstart (versión, hardware, tamaños, duración y resultado de cada comprobación)
- [ ] T116 [P] Actualizar `docs/glosario.md` con los términos que hayan aparecido en la implementación
- [ ] T117 [P] Crear `docs/pendientes-1.0.md` con lo que la constitución exige antes de la versión 1.0:
  - Evaluar la firma gratuita para código abierto (SignPath Foundation): requisitos, solicitud y decisión.
  - Hacer la prueba de uso de SC-007 (tarea T120).

### Validación final

- [ ] T118 Ejecutar los niveles 1 a 3 de `specs/001-system-disk-clone/quickstart.md` y corregir lo que falle
- [ ] T119 Ejecutar el nivel 4 de [quickstart.md](quickstart.md) en el segundo disco físico (⚠️ se borra) e incluir la desinstalación: comprobar que no quedan archivos en `Program Files\WinCopy`, entradas en Programas y características ni procesos o servicios (FR-032). Registrar el resultado en `docs/validacion-manual.md` (SC-001, SC-002, SC-006)
- [ ] T120 Prueba de uso de SC-007 (manual, antes de la 1.0) con al menos 5 personas sin perfil técnico: cada una identifica, antes de confirmar, qué disco se borrará en el paso 3. Registrar el protocolo y el resultado (≥ 90 % de aciertos) en `docs/validacion-manual.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sin dependencias.
- **Foundational (Phase 2)**: depende de Setup y **bloquea todas las historias**. Dentro de ella:
  - T019 → T021 → T022 → T023 → (T024, T025). Toda escritura de los spikes pasa ya por `DiskWriteGuard`
    con la restricción de discos virtuales activa.
  - El resultado de T024 condiciona T064, y el de T025 condiciona la decisión de BitLocker (R9).
- **Punto de control S2 (T073, dentro de US1)**: depende del orquestador T055 y de los adaptadores
  T057–T072. Valida T068 y T070 y **debe superarse antes de las tareas de interfaz** de la US1.
- **US1 (Phase 3)**: depende de Foundational. Es el MVP.
- **US2 (Phase 4)**: depende de Foundational y **reutiliza** el orquestador, el diseño y la interfaz de US1
  (las extiende). Requiere que US1 esté completa.
- **US3 (Phase 5)**: depende de Foundational y del orquestador de US1. Puede ir en paralelo con US2 (toca
  archivos distintos salvo `CloneExecution.cs` y las vistas de Progreso y Resultado, que conviene
  coordinar).
- **Polish (Phase 6)**: depende de las historias que se quieran entregar. T107 y T108 dependen de US1.

### Within Each User Story

- Los tests van primero y deben **fallar** antes de implementar.
- Dominio → aplicación → infraestructura → interfaz → textos.
- La historia se completa (checkpoint) antes de pasar a la siguiente prioridad.

### Parallel Opportunities

- Setup: T003–T008 en paralelo tras T001 y T002.
- Foundational: T009–T014, T016–T020 y T026–T028 en paralelo; los spikes T024 y T025 en
  paralelo entre sí tras T023.
- US1: los tests T032–T043, T045 y T046 en paralelo. Los adaptadores T057, T058, T062, T063 y T066–T069
  en paralelo, porque son archivos distintos.
- US2 y US3 pueden avanzar en paralelo una vez cerrada la US1.

---

## Parallel Example: User Story 1

```text
# Tests de dominio de US1 a la vez:
Task: "T032 Tests de PreflightEvaluator en tests/WinCopy.Domain.Tests/Planning/PreflightEvaluatorTests.cs"
Task: "T033 Tests de TargetLayoutCalculator en tests/WinCopy.Domain.Tests/Planning/TargetLayoutCalculatorTests.cs"
Task: "T034 Tests de ClonePlan en tests/WinCopy.Domain.Tests/Planning/ClonePlanTests.cs"
Task: "T036 Tests de CloneExecution en tests/WinCopy.Domain.Tests/Execution/CloneExecutionTests.cs"

# Adaptadores independientes a la vez:
Task: "T057 StorageInventoryReader en src/WinCopy.Infrastructure.Windows/Storage/StorageInventoryReader.cs"
Task: "T063 GptSerializer en src/WinCopy.Infrastructure.Windows/Gpt/GptSerializer.cs"
Task: "T067 TemporaryMounter en src/WinCopy.Infrastructure.Windows/Boot/TemporaryMounter.cs"
Task: "T068 OfflineRegistryEditor en src/WinCopy.Infrastructure.Windows/Boot/OfflineRegistryEditor.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Fase 1: Setup.
2. Fase 2: Foundational, **incluidos los spikes S1 y S3**. Si un spike falla, actualizar research.md y
   aplicar la alternativa antes de seguir.
3. Fase 3: US1, con el **punto de control S2** (arranque en VM) antes de construir la interfaz.
4. **STOP and VALIDATE**: niveles 1–3 de quickstart (unitarias, VHDX y VM).
5. Demo del MVP: clonar a un disco igual o mayor.

### Incremental Delivery

1. Setup + Foundational → base lista y riesgos técnicos despejados.
2. + US1 → MVP (clonar a un disco igual o mayor).
3. + US2 → destinos más pequeños (el caso HDD → SSD típico).
4. + US3 → cancelación y fallos con experiencia completa.
5. + Polish → verificación completa, README, release con atestaciones y validación en el disco físico.

---

## Notes

- [P] = archivos distintos y sin dependencias pendientes.
- Las etiquetas [USn] dan trazabilidad con spec.md.
- Pruebas de integración: marcadas con el rasgo `Integration`. Requieren una terminal de administrador y
  solo usan VHDX (protección T023).
- Ninguna tarea autoriza escribir en un disco físico salvo T119, que es manual y está confirmada por la
  persona usuaria.
- Hacer commit tras cada tarea o grupo lógico (Conventional Commits).
