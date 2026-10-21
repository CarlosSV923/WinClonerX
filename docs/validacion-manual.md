# Validación manual en disco físico

La constitución (principio IV) exige validar cada versión a mano en un disco físico real, incluido el arranque desde el clon. Esta página sigue el nivel 4 de la [guía de validación](../specs/001-system-disk-clone/quickstart.md) y guarda el resultado de cada ejecución.

**Atención:** la validación borra por completo el disco de destino. Usa un disco que no tenga nada que necesites.

## Cómo registrar una ejecución

1. Copia la plantilla de abajo, con el título de la nueva ejecución, al final de esta página. Las ejecuciones más recientes van al final.
2. Rellena cada campo. Si una comprobación no se hizo, escribe "No registrado". No marques nada que no hayas comprobado.
3. Anota el nombre del archivo `.clef` de la ejecución. Los registros están en `C:\ProgramData\WinCopy\logs`.
4. Haz commit del cambio.

## Plantilla

### Ejecución: <versión> (<fecha>)

| Campo | Valor |
|---|---|
| Versión de WinCopy | |
| Commit | |
| Fecha | |
| Equipo (fabricante y modelo) | |
| Firmware y tabla de particiones | UEFI, GPT |
| Windows (edición, versión, build e idioma) | |
| Origen (modelo, número de serie, tamaño, datos usados) | |
| Destino (modelo, número de serie, tamaño, conexión) | |
| Caso | Destino igual o mayor / Destino menor (reducción de C:) |
| Duración total (desde "Clonar ahora" hasta el resultado) | |
| Tiempo restante estimado al 10 % y duración real | |
| Tiempo desde abrir la aplicación hasta "Clonar ahora" | |
| Respuesta de la interfaz durante la copia | |
| Archivo de registro | |
| Resultado global | Aprobada / Con incidencias / Fallida |

| # | Comprobación | Requisito | Resultado | Notas |
|---|---|---|---|---|
| 1 | El disco del sistema no aparece como destino | FR-004 | | |
| 2 | El modelo y el número de serie del destino son los correctos | FR-002 | | |
| 3 | La revisión muestra particiones, tamaños, avisos y tiempo estimado | FR-007 | | |
| 4 | La confirmación pide los 4 últimos caracteres del número de serie | FR-012 | | |
| 5 | Desde abrir la aplicación hasta "Clonar ahora": menos de 3 minutos y 5 decisiones | SC-001 | | |
| 6 | Tiempo restante al 10 %: dentro de ±20 % de la duración real | SC-006 | | |
| 7 | La interfaz responde en menos de 100 ms durante la copia | Plan, T112 | | |
| 8 | La verificación estructural informa un resultado correcto | FR-022 | | |
| 9 | Windows arranca desde el disco clonado | SC-002 | | |
| 10 | C: es la partición de Windows, y los usuarios, programas y archivos están | SC-002 | | |
| 11 | `reagentc /info` muestra WinRE habilitado | Spike S2 | | |
| 12 | El menú de arranque tiene una sola entrada de Windows | Spike S2 | | |
| 13 | Con los dos discos conectados, ambos aparecen en línea | FR-017 | | |
| 14 | Si hubo reducción, C: vuelve a su tamaño original | FR-021a, SC-010 | | Solo en destino menor |
| 15 | La verificación completa detecta una alteración de datos | FR-022a, SC-009 | | Solo si se activó. Pendiente, T108 |
| 16 | Se puede exportar el registro | FR-026 | | Pendiente, T103 a T105 |
| 17 | Tras desinstalar no quedan archivos en `Program Files\WinCopy`, entradas en Programas ni procesos o servicios | FR-032 | | T119 |

Incidencias de esta ejecución:

| # | Descripción | Registro | Estado |
|---|---|---|---|
| | | | |

## Registros de ejecuciones

### Ejecución: 1.0.0+8467029 (2026-10-20)

Validación en hardware real del punto de control S2 (T073), con la aplicación completa. El detalle, con el registro de cada fase y las comprobaciones en el origen y en el clon, está en [el registro del punto de control S2](spikes/s2-arranque-hardware-real.md). Esta página no lo repite.

| Campo | Valor |
|---|---|
| Versión de WinCopy | 1.0.0+8467029 |
| Commit | 8467029 |
| Fecha | 2026-10-20 |
| Equipo | Mini PC con Windows 11 (es-MX), UEFI con GPT |
| Origen | Samsung SSD 990 EVO Plus de 1 TB. C: de 930 GiB con unos 112 GB usados |
| Destino | WD Green M.2 SATA de 960 GB (WDS960G2G0C), conectado dentro del equipo |
| Caso | Destino menor: reducción temporal de C: |
| Duración total | 8 min 54 s (la copia de datos tardó 8 min 29 s) |
| Resultado global | Con incidencias (ver abajo) |

| # | Comprobación | Resultado | Notas |
|---|---|---|---|
| 1 | El disco del sistema no aparece como destino | No registrado | |
| 2 | El modelo y el número de serie del destino son los correctos | No registrado | |
| 3 | La revisión muestra particiones, tamaños, avisos y tiempo estimado | No registrado | |
| 4 | La confirmación pide los 4 últimos caracteres del número de serie | No registrado | |
| 5 | Desde abrir la aplicación hasta "Clonar ahora": menos de 3 minutos | No registrado | |
| 6 | Tiempo restante al 10 %: dentro de ±20 % | No registrado | |
| 7 | La interfaz responde en menos de 100 ms | No registrado | |
| 8 | La verificación estructural informa un resultado correcto | Sí | Correcta, en 175 ms |
| 9 | Windows arranca desde el disco clonado | Sí | Arranca directamente, sin el menú "Elegir un sistema operativo" |
| 10 | C: es la partición de Windows, y los usuarios, programas y archivos están | Parcial | C: correcta. `chkdsk C: /scan` sin errores (396 414 archivos, 0 sectores defectuosos). Usuarios y programas: no registrado |
| 11 | `reagentc /info` muestra WinRE habilitado | Sí | WinRE habilitado en la partición de recuperación del clon, y arranca |
| 12 | El menú de arranque tiene una sola entrada de Windows | Sí | Un solo cargador |
| 13 | Con los dos discos conectados, ambos aparecen en línea | No registrado | |
| 14 | Si hubo reducción, C: vuelve a su tamaño original | Sí | 999 000 375 296 bytes, el tamaño original |
| 15 | La verificación completa detecta una alteración de datos | No aplica | No estaba activada |
| 16 | Se puede exportar el registro | No disponible | El botón está desactivado en esta versión |
| 17 | Desinstalación sin restos | No registrado | |

Incidencias de esta ejecución:

| # | Descripción | Registro | Estado |
|---|---|---|---|
| 1 | La aplicación se cerró durante la fase PrepareTarget, con C: ya reducido. No se ha vuelto a reproducir y no hay datos para saber la causa. Desde 37757d8, el diario de restauración devuelve C: a su tamaño al iniciar la aplicación si vuelve a pasar. | No disponible | Abierta |
| 2 | La cancelación no se validó en hardware. Corresponde al commit f5fe7ca. | No aplica | Pendiente |
| 3 | El diario de restauración no se validó en hardware. Esta versión no lo escribía todavía (corresponde al commit 37757d8). | No aplica | Pendiente |
| 4 | La salida de `bcdedit` y `reagentc` llega con los acentos mal, porque se lee con la página de códigos por defecto. El análisis no se ve afectado. | No aplica | Abierta |

Los hallazgos corregidos durante la validación (inicio colgado, mapa de bits de NTFS, letra automática y reapunte del BCD) están en el mismo registro del punto de control S2.
