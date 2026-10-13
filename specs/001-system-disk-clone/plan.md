# Implementation Plan: Clonado del disco del sistema

**Branch**: `001-system-disk-clone` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-system-disk-clone/spec.md`

## Summary

WinCopy clona el disco del sistema Windows (UEFI/GPT) a otro disco mientras Windows está en ejecución.
Funcionamiento:

1. Toma una instantánea VSS coherente de los volúmenes NTFS del origen.
2. Copia solo los clústeres ocupados (más las particiones EFI y MSR en bruto) a sus nuevas posiciones en
   el destino, que está fuera de línea.
3. Escribe la tabla GPT al final, como "compromiso". Un clon a medias nunca tiene tabla de particiones.
4. Genera identificadores nuevos y ajusta el registro del clon para que su Windows siga siendo `C:`.
5. Regenera el arranque con `bcdboot /s` (sin tocar la NVRAM del firmware) y WinRE con `reagentc`.
6. Verifica la estructura y, de forma opcional, el contenido completo.

Para destinos más pequeños, antes de clonar se reduce la partición de Windows del origen con la Storage
Management API, y se restaura al terminar sea cual sea el resultado.

Arquitectura: DDD con contextos de Inventario, Planificación, Ejecución y Reparación de arranque, y una
arquitectura hexagonal con un dominio puro, casos de uso con puertos, adaptadores Windows (P/Invoke, COM
y WMI) y una interfaz WPF MVVM tipo asistente. Detalles en [research.md](research.md) y
[data-model.md](data-model.md).

## Technical Context

**Language/Version**: C# 14 sobre .NET 10 (SDK 10.0.401), `net10.0-windows`, solo x64

**Primary Dependencies**:
- WPF con el tema Fluent integrado.
- CommunityToolkit.Mvvm (MIT).
- Microsoft.Extensions.Hosting y DependencyInjection (MIT).
- System.Management (MIT) para la Storage Management API, BitLocker y WMI.
- Serilog + Serilog.Formatting.Compact + Serilog.Sinks.File (Apache-2.0).
- Interop propia generada en compilación (`LibraryImport`, `GeneratedComInterface`) para Win32, IOCTL,
  VSS, `virtdisk` y el registro.
- WiX Toolset v6 (MS-RL) para el MSI.

**Storage**: N/A. Solo archivos de registro en `%ProgramData%\WinCopy\logs` (CLEF). Sin base de datos ni
configuración persistente.

**Testing**:
- xUnit v3 + Shouldly. NetArchTest.Rules (MIT) para los tests de arquitectura (R15).
- Fakes propios de los puertos, con inyección de fallos.
- Integración sobre VHDX (`virtdisk.dll`) como administrador. La protección contra discos físicos está en
  dos capas: `DiskWriteGuard` en producción y una restricción "solo VHDX" registrada para todo el proceso
  de pruebas (R15).
- Arranque en VM Gen2 de Hyper-V (opcional) y validación manual en disco físico
  ([quickstart.md](quickstart.md)).

**Target Platform**: Windows 10 22H2 y Windows 11 x64, arrancados en UEFI con disco del sistema GPT. Se
ejecuta como administrador.

**Project Type**: aplicación de escritorio (WPF) con instalador MSI

**Performance Goals** (medidos en la tarea de rendimiento de la fase de pulido de `tasks.md`):
- Rendimiento de copia ≥ 85 % de la velocidad secuencial del disco más lento (tubería de 4 MiB con 2–4
  búferes en vuelo).
- Duración proporcional a los datos ocupados (SC-003).
- La interfaz responde en < 100 ms durante la copia.
- Tiempo restante ±20 % desde el 10 % (SC-006).

**Constraints**:
- Sin drivers de kernel.
- Sin red ni telemetría.
- Solo APIs y herramientas documentadas por Microsoft.
- Memoria < 300 MB durante la copia.
- El disco de origen solo se modifica en ShrinkSource y RestoreSource.
- Las pruebas automáticas nunca abren discos físicos.

**Scale/Scope**:
- 1 usuario y 1 operación a la vez.
- Discos de hasta 16 TB y GPT de hasta 128 particiones.
- 5 pantallas del asistente.
- 2 idiomas.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Evaluado contra la constitución **v1.1.1**.

| Principio | Cómo lo cumple el diseño | Estado |
|-----------|--------------------------|--------|
| **I. Seguridad de los datos (NO NEGOCIABLE)** | Invariante de dominio "destino ≠ sistema", repetida en `ITargetDiskFactory` y en los adaptadores (defensa en profundidad). Confirmación con los últimos caracteres del número de serie (R13). Reducción del origen con confirmación propia. Comprobaciones previas bloqueantes ante cualquier duda. "Compromiso" GPT al final y compensaciones (R4). Verificación estructural siempre. | ✅ PASS |
| **II. Planificar antes de actuar** | `ClonePlan` inmutable, calculado sin tocar discos. Se muestra en el paso 2 y se confirma en el 3. La huella del inventario invalida el plan si algo cambia (FR-011). | ✅ PASS |
| **III. Dominio aislado (DDD + hexagonal)** | `WinCopy.Domain` sin referencias a Windows ni WPF (lo comprueba un test de arquitectura). Cuatro contextos delimitados. Los puertos están en `WinCopy.Application` y los adaptadores en `WinCopy.Infrastructure.Windows`. Glosario en `docs/glosario.md`. | ✅ PASS |
| **IV. Pruebas primero, con discos virtuales** | TDD en el dominio y los casos de uso. Integración solo sobre VHDX creados por el fixture. Protección por `BusType` y ruta del VHDX. Lista de validación manual en disco físico. | ✅ PASS |
| **V. Solo APIs públicas (sala limpia)** | VSS, IOCTL y FSCTL documentados, Storage Management API, `bcdboot` y `reagentc`, `RegLoadKey` y `virtdisk`. Sin ingeniería inversa, sin código GPL y sin drivers. Dependencias con licencias MIT, Apache-2.0, BSD o MS-RL (justificadas en R11, R14, R15 y R16). | ✅ PASS |
| **VI. Alcance mínimo** | Solo clonar el disco del sistema en UEFI/GPT. BIOS/MBR, discos dinámicos y Espacios de almacenamiento se rechazan de forma explícita. Sin funciones adicionales. | ✅ PASS |
| **VII. Transparencia y observabilidad** | Eventos estructurados ([contracts/log-events.md](contracts/log-events.md)), errores con código localizable y detalle técnico en el log, progreso real en bytes y exportación del registro. Sin red. | ✅ PASS |
| **VIII. Experiencia guiada y localizada** | Asistente de 5 pasos ([contracts/ui-wizard.md](contracts/ui-wizard.md)). Recursos `.resx` en español e inglés con test de paridad. Accesibilidad por teclado y lector de pantalla en la selección y la confirmación. | ✅ PASS |
| **Restricciones técnicas** | .NET 10 + WPF MVVM, x64, `requireAdministrator`, MSI con WiX, `Nullable` + `TreatWarningsAsErrors` + analizadores. Distribución sin firmar con SHA-256 y attestations. | ✅ PASS |

**Re-check post-design (Fase 1)**: ✅ PASS. El diseño no introduce desviaciones. La referencia a
`FSCTL_SHRINK_VOLUME` del principio I se corrigió a `MSFT_Partition.Resize` con la enmienda PATCH v1.1.1,
a raíz de R6.

## Project Structure

### Documentation (this feature)

```text
specs/001-system-disk-clone/
├── plan.md              # Este archivo
├── research.md          # Fase 0: decisiones técnicas y spikes
├── data-model.md        # Fase 1: modelo de dominio
├── quickstart.md        # Fase 1: guía de validación
├── contracts/
│   ├── ports.md         # Puertos hexagonales y casos de uso
│   ├── ui-wizard.md     # Contrato de pantallas del asistente
│   └── log-events.md    # Esquema del registro
├── checklists/
│   └── requirements.md  # Revisión de calidad de la especificación
└── tasks.md             # Fase 2 (/speckit-tasks, aún no creado)
```

### Source Code (repository root)

```text
WinCopy.slnx
Directory.Build.props            # Nullable, TreatWarningsAsErrors, analizadores, x64
Directory.Packages.props         # Gestión centralizada de versiones de paquetes

src/
├── WinCopy.Domain/              # Sin dependencias externas
│   ├── Shared/                  # ByteSize, Extent, DiskId, GUID, Result
│   ├── Inventory/               # DiskInventory, Disk, Partition, Volume
│   ├── Planning/                # ClonePlan, PartitionMapping, PreflightEvaluator,
│   │                            # TargetLayoutCalculator, ClonePlanner, CapacityPolicy
│   ├── Execution/               # CloneExecution, fases, estados, eventos, CloneResultReport
│   └── BootRepair/              # BootRepairSpec, MountedDevicesRewrite
├── WinCopy.Application/         # Casos de uso y puertos (contracts/ports.md)
│   ├── Ports/
│   └── UseCases/                # GetInventoryAndEligibility, BuildClonePlan, ConfirmClonePlan,
│                                # ExecuteClone (orquestador + compensaciones), CancelExecution, ExportLog
├── WinCopy.Infrastructure.Windows/
│   ├── Interop/                 # LibraryImport/GeneratedComInterface: kernel32, vssapi, advapi32, virtdisk
│   ├── Storage/                 # Inventario y redimensionado (System.Management, MSFT_*)
│   ├── Snapshots/               # VSS
│   ├── RawIo/                   # Lectura de origen, ITargetDisk, búferes alineados, tubería de copia
│   ├── Gpt/                     # Serializador MBR protector + GPT + CRC32 (puro, probado)
│   ├── Boot/                    # bcdboot, reagentc, registro fuera de línea, montajes temporales
│   ├── Platform/                # Firmware, energía, BitLocker, monitor de dispositivos
│   └── Logging/                 # IExecutionLog con Serilog
└── WinCopy.App/                 # WPF (único proyecto con UI)
    ├── app.manifest             # requireAdministrator
    ├── Views/  ViewModels/      # Pasos del asistente
    ├── Controls/                # DiskCard, PartitionBar, StepRail
    ├── Themes/                  # Paleta y estilos propios sobre Fluent
    └── Resources/               # Strings.resx, Strings.es.resx

tests/
├── WinCopy.Domain.Tests/
├── WinCopy.Application.Tests/           # Orquestador con fakes e inyección de fallos
├── WinCopy.Infrastructure.Windows.Tests/ # Partes puras: GPT/CRC32, parseo del mapa de clústeres, formato del registro
├── WinCopy.Infrastructure.IntegrationTests/ # VHDX (administrador), protección contra discos físicos, spikes
├── WinCopy.App.Tests/                   # ViewModels, paridad de recursos, sin literales en XAML
└── WinCopy.ArchitectureTests/           # El dominio no referencia Windows/WPF; dependencias permitidas

installer/
└── WinCopy.Installer/           # Proyecto WiX v6 (MSI x64 por máquina)

docs/
├── decisiones-iniciales.md
├── glosario.md                  # Lenguaje ubicuo
└── validacion-manual.md         # Registro de validaciones en disco físico
```

**Structure Decision**: aplicación de escritorio con cuatro proyectos de producción que materializan la
arquitectura hexagonal: Domain → Application → Infrastructure.Windows / App, con dependencias solo hacia
dentro. `WinCopy.App` es la raíz de composición (DI). Hay un proyecto de tests por proyecto de producción,
más integración y arquitectura. El instalador queda aparte, en `installer/`.

## Fases de entrega (orientativas para /speckit-tasks)

1. **Spikes técnicos**:
   - S1 (escritura en disco fuera de línea + GPT propia) y S3 (BitLocker, con una instantánea WMI) se
     hacen sobre VHDX en la fase Foundational.
   - S2 (arranque y WinRE del clon en una VM) es un punto de control al final de la parte técnica de la
     US1, porque necesita el motor de clonado completo.
   - Deciden entre la ruta principal y la alternativa de R4, R7 y R9.
2. **Base**: solución, `Directory.Build.props`, tests de arquitectura, objetos de valor compartidos,
   glosario.
3. **US1 (P1)**: inventario, comprobaciones previas, plan con destino igual o mayor, ejecución completa,
   verificación estructural, asistente completo, MSI.
4. **US2 (P2)**: estrategia de reducción del origen, confirmación adicional, restauración como
   compensación.
5. **US3 (P3)**: cancelación, detección de desconexión, errores de E/S, exportación del registro,
   verificación completa opcional.
6. **Pulido**: accesibilidad, revisión de textos es/en, README (instalación sin firma, constitución
   v1.1.0), validación manual en el disco físico.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

No hay violaciones que justificar.
