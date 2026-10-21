# Punto de control S2: arranque de un clon real

**Resultado: superado (2026-10-20).** El clon arranca, Windows queda en `C:`, el menú de arranque tiene una sola
entrada y WinRE está habilitado y arranca.

## Por qué en hardware real y no en una VM

`tasks.md` (T073) preveía validar S2 en una VM Gen2 de Hyper-V con un prototipo de línea de comandos. Al final se
validó con la aplicación completa sobre discos físicos:

- Las pruebas de integración con VHDX provocaban pantallas azules en el equipo de desarrollo (ver la memoria del
  proyecto).
- Cuando llegó el momento de validar, la interfaz ya estaba hecha, así que el prototipo de línea de comandos ya no
  hacía falta.

Un disco físico arrancando en un equipo real es una prueba más exigente que una VM.

## Montaje

| | |
|---|---|
| Equipo | Mini PC con Windows 11 (es-MX), UEFI + GPT |
| Origen | Samsung SSD 990 EVO Plus 1 TB, `C:` de 930 GiB con unos 112 GB usados |
| Destino | WD Green M.2 SATA de 960 GB (`WDS960G2G0C`), conectado por dentro |
| Versión | `1.0.0+8467029` (reapunte del BCD en su sitio) |
| Plan | Reducción temporal del origen (US2): el destino es menor que el origen |

## Ejecución (registro `.clef`)

| Fase | Duración | Detalle |
|---|---|---|
| ShrinkSource | 14,6 s | `C:` reducido a 169 GB |
| Snapshot | 1,5 s | VSS |
| PrepareTarget | 24 ms | Bloqueo, sin conexión y borrado de ambos extremos |
| CopyData | 8 min 29 s | 124 GB del volumen de Windows, unos 246 MB/s |
| CommitLayout | 24 ms | GPT escrita al final y disco en línea |
| ExpandFileSystem | 355 ms | NTFS ampliado a 893 GiB |
| RepairBoot | 1,8 s | 5 lecturas y 9 escrituras con `bcdedit /store Z:\EFI\Microsoft\Boot\BCD`, más `reagentc /setreimage` |
| StructuralVerification | 175 ms | Correcta |
| RestoreSource | 5,9 s | `C:` vuelve a 999 000 375 296 bytes |
| **Total** | **8 min 54 s** | `ExecutionCompleted`, `Completed` |

El reapunte del BCD conservó los identificadores del origen:

- cargador `{b05f0745…}`;
- reanudación `{b05f0744…}`;
- WinRE `{b05f0746…}`, con sus opciones de ramdisk `{b05f0749…}`.

La comprobación final mostró `device` y `osdevice` como `partition=E:`, que era la letra temporal del clon.

## Comprobaciones en el clon arrancado

| Comprobación | Resultado |
|---|---|
| Menú de arranque | Arranca directamente, sin el menú "Elegir un sistema operativo" |
| `bcdedit /enum` | Un solo cargador (`{current}`, `partition=C:`), con `locale es-MX`, `recoverysequence` y `hypervisorlaunchtype Auto` |
| `reagentc /info` | `Enabled`, en `\\?\GLOBALROOT\device\harddisk0\partition4\Recovery\WindowsRE` (la partición del propio clon) y con el id `b05f0746…` |
| WinRE en la práctica | "Inicio avanzado" muestra la pantalla "Elegir una opción" |
| Particiones | EFI de 200 MiB, MSR de 16 MiB, Windows de 893 GiB y recuperación de 930 MiB, que termina 344 KiB antes del final, como en el plan |
| `chkdsk C: /scan` | "Se examinó el sistema de archivos sin encontrar problemas"; 396 414 archivos y 0 sectores defectuosos |
| Letra de Windows | `C:` |

## Comprobaciones en el origen

| Comprobación | Resultado |
|---|---|
| Tamaño de `C:` | 999 000 375 296 bytes, el original |

## Hallazgos que llevaron hasta aquí (todos corregidos)

1. **Inicio colgado**: un nombre de propiedad reservado del registro detenía la ejecución antes de empezar (`a52bb1b`).
2. **Mapa de bits de NTFS**: en volúmenes grandes se lee en varios trozos (`2cfae5f`).
3. **Letra automática de Windows**: el volumen clonado recibe una letra automática y hay que reutilizarla. Además,
   el borrado del destino en línea no puede escribir dentro de una partición (`6874a71`).
4. **BCD con dos entradas y WinRE desactivado**: se cambió a reapuntar el almacén en su sitio (`8467029`, research R7).

## Pendiente de validar en hardware real

- **Cancelación (US3)**: `f5fe7ca`.
- **Diario de restauración del origen tras un cierre inesperado**: `37757d8`.

  Esta prueba se hizo con `8467029`, que no incluye ninguno de los dos. El diario figura como no pendiente
  (`False`), pero esta versión todavía no escribía el diario.
- **El cierre inesperado del mini PC en la fase PrepareTarget**: ocurrió con `8467029` y no se ha vuelto a
  reproducir. No hay datos para saber la causa (ni el registro ni el visor de eventos). Desde `37757d8`, si vuelve
  a pasar, el diario restaura el origen al iniciar.
- **Codificación de las herramientas**: la salida de `bcdedit` y `reagentc` se lee con la página de códigos por
  defecto y no con la OEM, así que los acentos llegan mal al registro (por ejemplo, "operaci¢n"). El análisis no se
  ve afectado, porque los nombres de los elementos están en ASCII.
