# Validación manual en disco físico

La constitución (principio IV) exige validar cada versión a mano en un disco físico real, incluido el arranque desde el clon. Esta página sigue el nivel 4 de la [guía de validación](../specs/001-system-disk-clone/quickstart.md) y guarda el resultado de cada ejecución.

**Atención:** la validación borra por completo el disco de destino. Usa un disco que no tenga nada que necesites.

## Cómo registrar una ejecución

1. Copia la plantilla de abajo, con el título de la nueva ejecución, al final de esta página. Las ejecuciones más recientes van al final.
2. Rellena cada campo. Si una comprobación no se hizo, escribe "No registrado". No marques nada que no hayas comprobado.
3. Anota el nombre del archivo `.clef` de la ejecución. Los registros están en `C:\ProgramData\WinClonerX\logs`.
4. Haz commit del cambio.

## Plantilla

### Ejecución: <versión> (<fecha>)

| Campo | Valor |
|---|---|
| Versión de WinClonerX | |
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
| 17 | Tras desinstalar no quedan archivos en `Program Files\WinClonerX`, entradas en Programas ni procesos o servicios | FR-032 | | T119 |

Incidencias de esta ejecución:

| # | Descripción | Registro | Estado |
|---|---|---|---|
| | | | |

## Registros de ejecuciones

### Ejecución: 1.0.0+8467029 (2026-10-20)

Validación en hardware real del punto de control S2 (T073), con la aplicación completa. El detalle, con el registro de cada fase y las comprobaciones en el origen y en el clon, está en [el registro del punto de control S2](spikes/s2-arranque-hardware-real.md). Esta página no lo repite.

| Campo | Valor |
|---|---|
| Versión de WinClonerX | 1.0.0+8467029 |
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

## Revisión de accesibilidad (T109)

Se revisaron los cinco pasos, el cambio de página mediante `CurrentPage`, los controles de `Views/`, los componentes de `Controls/` y los recursos del tema. El paso 1 enfoca la primera tarjeta apta cuando termina la lectura de discos; las tarjetas aceptan Tab, Mayús/Tab, Intro y Espacio y muestran un borde de foco. Los pasos 2 a 5 enfocan respectivamente el título, el campo de confirmación, Cancelar y Exportar registro al aparecer.

La barra de particiones expone su nombre localizado y el detalle de particiones, tamaños, espacio usado y espacio sin asignar. Los discos incluyen nombre y ayuda con estado y motivo. Las fases y el porcentaje anuncian cambios con prioridad educada; los errores y avisos que aparecen tras una acción usan prioridad asertiva.

### Contraste WCAG 2.1

Los colores se leen de `WinClonerXTheme.xaml`. El texto normal requiere 4.5:1; los bordes de controles y el texto grande requieren 3:1. Los controles desactivados están exentos, pero el texto desactivado se mantuvo legible.

| Elemento | Primer plano | Fondo | Contraste | Umbral |
|---|---|---|---:|---:|
| Texto principal | `#1B2433` (Texto principal) | `#FFFFFF` (Superficie blanca) | 15.59:1 | 4.5:1 |
| Texto principal | `#1B2433` (Texto principal) | `#F4F6FA` (Fondo claro) | 14.41:1 | 4.5:1 |
| Texto secundario | `#5B6577` (Texto secundario) | `#FFFFFF` (Superficie blanca) | 5.88:1 | 4.5:1 |
| Texto secundario | `#5B6577` (Texto secundario) | `#F4F6FA` (Fondo claro) | 5.43:1 | 4.5:1 |
| Texto desactivado | `#626D7D` (Texto desactivado) | `#FFFFFF` (Superficie blanca) | 5.25:1 | 4.5:1 |
| Texto desactivado | `#626D7D` (Texto desactivado) | `#F4F6FA` (Fondo claro) | 4.85:1 | 4.5:1 |
| Texto de la barra lateral | `#FFFFFF` (Texto de la barra lateral) | `#14213D` (Barra lateral) | 15.97:1 | 4.5:1 |
| Texto de la barra lateral | `#FFFFFF` (Texto de la barra lateral) | `#5B6E91` (Círculo de paso) | 5.14:1 | 4.5:1 |
| Borde del círculo de paso | `#5B6E91` (Círculo de paso) | `#14213D` (Barra lateral) | 3.11:1 | 3:1 |
| Acción principal y paso actual | `#1F6FEB` (Acción principal y paso actual) | `#14213D` (Barra lateral) | 3.45:1 | 3:1 |
| Texto de la barra lateral | `#FFFFFF` (Texto de la barra lateral) | `#1F6FEB` (Acción principal y paso actual) | 4.63:1 | 4.5:1 |
| Texto de la barra lateral | `#FFFFFF` (Texto de la barra lateral) | `#C62828` (Texto de error) | 5.62:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#5874A8` (Acción principal desactivada) | 4.69:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#985D59` (Acción de borrado desactivada) | 5.19:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#1F6FEB` (Acción principal y paso actual) | 4.63:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#1859C7` (Acción principal al pasar el puntero) | 6.38:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#C62828` (Texto de error) | 5.62:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#B3261E` (Acción de borrado al pasar el puntero) | 6.54:1 | 4.5:1 |
| Texto blanco | `#FFFFFF` (Texto blanco) | `#16733F` (Insignia correcta) | 5.90:1 | 4.5:1 |
| Texto de error | `#C62828` (Texto de error) | `#FFFFFF` (Superficie blanca) | 5.62:1 | 4.5:1 |
| Texto de error | `#C62828` (Texto de error) | `#F4F6FA` (Fondo claro) | 5.20:1 | 4.5:1 |
| Foco del teclado | `#0B3D91` (Foco del teclado) | `#FFFFFF` (Superficie blanca) | 10.04:1 | 3:1 |
| Foco del teclado | `#0B3D91` (Foco del teclado) | `#F4F6FA` (Fondo claro) | 9.28:1 | 3:1 |
| Borde de control | `#7D899B` (Borde de control) | `#FFFFFF` (Superficie blanca) | 3.55:1 | 3:1 |
| Borde de control | `#7D899B` (Borde de control) | `#F4F6FA` (Fondo claro) | 3.28:1 | 3:1 |

### Cambios de color

| Recurso | Antes | Después | Contraste resultante |
|---|---|---|---:|
| `Brush.AccentDisabled` | `#A9C5F5` | `#5874A8` | 4.69:1 con texto blanco |
| `Brush.Danger` | `#D93025` | `#C62828` | 5.62:1 sobre blanco; 5.20:1 sobre fondo claro |
| `Brush.DangerDisabled` | `#EBA7A2` | `#985D59` | 5.19:1 con texto blanco |
| `Brush.Success` | `#2E9E5B` | `#16733F` | 5.90:1 con texto blanco |
| `Brush.Border` | `#D8DEE9` | `#7D899B` | 3.55:1 sobre blanco; 3.28:1 sobre fondo claro |
| `Brush.RailMuted` | `#2A3A5E` | `#5B6E91` | 3.11:1 frente a la barra lateral; 5.14:1 con texto blanco |
| `Brush.TextDisabled` | `#A3ABBA` | `#626D7D` | 5.25:1 sobre blanco; 4.85:1 sobre fondo claro |

Queda pendiente la comprobación manual con Accessibility Insights y Narrator en un equipo Windows real. Hay que comprobar el foco, la navegación completa, los nombres anunciados, los cambios de progreso y los avisos en pantalla.

### Comprobación en hardware real (2026-10-22)

- **Teclado:** recorrido de los pasos 1 a 3 solo con el teclado (Tab, Espacio, Intro): correcto. Foco inicial en la tarjeta del disco y en la caja de confirmación, con el foco siempre visible.
- **Colores:** bordes de las tarjetas, botones desactivados, rojo, verde y círculos de los pasos: correctos y legibles.
- **Pendiente:** Accessibility Insights y Narrator.

## Revisión editorial (T110)

Propuesta de Codex (gpt-6-luna) revisada y aplicada el 2026-10-21. Una primera reescritura de 140 textos se descartó porque tenía errores gramaticales, perdía datos útiles (UEFI, GPT, tamaño de sector) y daba un consejo equivocado. En esta segunda pasada se cambiaron 20 claves en cada idioma. Tres textos tenían un significado incorrecto o engañoso:

- **Disks_Strategy_Extend** prometía que Windows se ampliaría siempre, pero con un destino del mismo tamaño no se amplía.
- **La restauración del origen** se presentaba como segura, pero puede fallar (el resultado lo muestra aparte). Ahora dice que WinClonerX «intentará» restaurarla.
- **El aviso de WinRE** pedía ejecutar `reagentc /enable` sin decir dónde. Ejecutado antes de arrancar desde el clon, actuaría sobre el Windows de origen.

| Clave | Idioma | Antes | Después |
|---|---|---|---|
| Disks_Subtitle | es | El disco del sistema es el origen. Elige el disco que recibirá la copia. Todo lo que haya en el disco de destino se borrará. | El disco donde está instalado Windows es el disco de origen. Elige el disco de destino, que recibirá el clon. Se borrará todo su contenido. |
| Disks_Subtitle | en | Your system disk is the source. Pick the disk that will receive the copy. Everything on the destination disk will be erased. | The disk where Windows is installed is the source disk. Choose the target disk for the clone. Everything on it will be erased. |
| Disks_Source_Heading | es | Disco del sistema (origen) | Disco de origen |
| Disks_Source_Heading | en | System disk (source) | Source disk |
| Disks_Strategy_Extend | es | Se ampliará Windows | No hace falta reducir la partición de Windows |
| Disks_Strategy_Extend | en | Windows will be extended | The Windows partition does not need to be shrunk |
| Disks_Strategy_Shrink | es | Hay que reducir Windows en el disco original | Se reducirá temporalmente la partición de Windows del disco de origen |
| Disks_Strategy_Shrink | en | Windows on the original disk must be shrunk first | The Windows partition on the source disk must be shrunk temporarily |
| Preflight_SourceNotGpt | es | El disco del sistema no usa el estilo de particiones GPT, que esta versión necesita. | El disco de origen no usa GPT, un requisito de esta versión. |
| Preflight_SourceNotGpt | en | The system disk does not use the GPT partition style, which this version requires. | The source disk does not use GPT, which this version requires. |
| Preflight_MultipleWindows | es | El disco del sistema no contiene exactamente una instalación de Windows, así que no hay nada claro que clonar. | El disco de origen no tiene una única partición de Windows identificable. WinClonerX no puede determinar qué clonar. |
| Preflight_MultipleWindows | en | The system disk does not contain exactly one Windows installation, so there is nothing clear to clone. | The source disk does not have exactly one identifiable Windows partition. WinClonerX cannot determine what to clone. |
| Preflight_SectorSizeMismatch | es | Este disco tiene un tamaño de sector distinto al del disco del sistema, y esta versión no puede clonar entre ambos. | El disco de destino tiene un tamaño de sector lógico distinto al del disco de origen. WinClonerX solo puede clonar entre discos con el mismo tamaño de sector lógico. Elige un disco compatible. |
| Preflight_SectorSizeMismatch | en | This disk uses a different sector size than the system disk, and this version cannot clone between them. | The target disk has a different logical sector size from the source disk. WinClonerX can clone only between disks with the same logical sector size. Choose a compatible disk. |
| Preflight_SnapshotUnavailable | es | El sistema no puede crear la copia coherente que necesita mientras Windows está en uso. Comprueba que la Instantánea de volumen (VSS) está disponible y vuelve a intentarlo. | No se pudo crear la instantánea que necesita el clonado mientras Windows está en uso. Comprueba que el servicio de instantáneas de volumen (VSS) está disponible y vuelve a intentarlo. |
| Preflight_SnapshotUnavailable | en | The system cannot create the consistent copy it needs while Windows is running. Check that Volume Shadow Copy is available, then try again. | The snapshot needed for cloning could not be created while Windows is running. Check that Volume Shadow Copy (VSS) is available, then try again. |
| Review_Before | es | Antes (origen) | Antes (disco de origen) |
| Review_Before | en | Before (source) | Before (source disk) |
| Review_After | es | Después (destino) | Después (disco de destino) |
| Review_After | en | After (destination) | After (target disk) |
| Shrink_Heading | es | Reducción temporal del disco original | Reducción temporal de la partición de Windows del disco de origen |
| Shrink_Heading | en | Temporary shrink of the original disk | Temporary shrink of the Windows partition on the source disk |
| Shrink_Review_Body | es | Windows (C:) pasará de {0} a {1} durante la copia y se restaurará al terminar | La partición de Windows del disco de origen pasará de {0} a {1} antes del clonado. WinClonerX intentará devolverla a su tamaño original al terminar. |
| Shrink_Review_Body | en | Windows (C:) will go from {0} to {1} during the copy and will be restored when it finishes. | The Windows partition on the source disk will go from {0} to {1} before cloning. WinClonerX will try to restore its original size when cloning finishes. |
| Shrink_Confirm_Body | es | Para que quepa, WinClonerX reducirá temporalmente la partición de Windows del disco original de {0} a {1}. Al terminar la devolverá a su tamaño. Windows puede tardar varios minutos en mover archivos. | Para que el clon quepa, WinClonerX reducirá temporalmente la partición de Windows del disco de origen de {0} a {1}. Al terminar, intentará devolverla a su tamaño original. Windows puede tardar varios minutos en mover archivos. |
| Shrink_Confirm_Body | en | To make it fit, WinClonerX will temporarily shrink the Windows partition on the original disk from {0} to {1}. When it finishes, it gives the space back and restores its size. Windows may take several minutes to move files. | To make the clone fit, WinClonerX will temporarily shrink the Windows partition on the source disk from {0} to {1}. When cloning finishes, it will try to restore the partition to its original size. Windows may take several minutes to move files. |
| Cancel_Confirm_Message | es | ¿Cancelar el clonado? El disco de destino quedará sin inicializar. Tu disco original no se modifica (si se redujo, se restaurará su tamaño). | ¿Cancelar el clonado? El disco de destino quedará sin inicializar. El disco de origen no se modifica; si WinClonerX redujo su partición de Windows, intentará restaurarla. |
| Cancel_Confirm_Message | en | Cancel the clone? The target disk will be left uninitialized. Your original disk is not modified (if it was shrunk, its size is restored). | Cancel cloning? The target disk will be left uninitialized. The source disk is not modified; if WinClonerX shrank its Windows partition, it will try to restore it. |
| Error_ShrinkInsufficient | es | Windows no se puede reducir lo suficiente para caber en el disco de destino. Libera espacio en el disco del sistema o elige un disco más grande. | No se puede reducir lo suficiente la partición de Windows del disco de origen para que quepa en el disco de destino. Libera espacio en el disco de origen o elige un disco de destino más grande. |
| Error_ShrinkInsufficient | en | Windows cannot be shrunk enough to fit on the target disk. Free up space on the system disk or choose a larger disk. | The Windows partition on the source disk cannot be shrunk enough to fit on the target disk. Free up space on the source disk or choose a larger target disk. |
| Phase_ShrinkSource | es | Reduciendo Windows en el disco original | Reduciendo la partición de Windows del disco de origen |
| Phase_ShrinkSource | en | Shrinking Windows on the original disk | Shrinking the Windows partition on the source disk |
| Phase_RestoreSource | es | Restaurando el tamaño del disco original | Restaurando el tamaño de la partición de Windows del disco de origen |
| Phase_RestoreSource | en | Restoring the size of the original disk | Restoring the size of the Windows partition on the source disk |
| Result_SourceRestore_Failed | es | No se pudo devolver Windows (C:) a su tamaño original. El clon es válido. Puedes recuperar el espacio desde Administración de discos → clic derecho en C: → Extender volumen | No se pudo restaurar el tamaño original de la partición de Windows del disco de origen. El clon es válido. Para recuperar el espacio, abre Administración de discos, haz clic derecho en C: y elige «Extender volumen». |
| Result_SourceRestore_Failed | en | Windows (C:) could not be returned to its original size. The clone is valid. You can recover the space in Disk Management → right-click C: → Extend Volume. | The Windows partition on the source disk could not be restored to its original size. The clone is valid. To recover the space, open Disk Management, right-click C: and choose “Extend Volume.” |
| Result_Recovery_Warning | es | No se pudo configurar WinRE en el disco nuevo. Windows arrancará igualmente, pero el entorno de recuperación no funcionará hasta que lo actives. Para hacerlo, abre el símbolo del sistema como administrador y ejecuta: reagentc /enable | No se pudo configurar WinRE en el disco de destino. Windows arrancará igualmente, pero el entorno de recuperación no funcionará hasta que lo actives. Después de iniciar Windows desde el clon, abre el símbolo del sistema como administrador y ejecuta: reagentc /enable |
| Result_Recovery_Warning | en | WinRE could not be configured on the new disk. Windows will still start, but the recovery environment will not work until you enable it. To do it, open a command prompt as administrator and run: reagentc /enable | WinRE could not be configured on the target disk. Windows will still start, but the recovery environment will not work until you enable it. After starting Windows from the clone, open a command prompt as administrator and run: reagentc /enable |
| Error_RecoveryConfigFailed | es | No se pudo configurar WinRE en el clon. Windows arrancará igualmente. Activa WinRE más tarde con reagentc /enable. | No se pudo configurar WinRE en el clon. Windows arrancará igualmente. Después de iniciar Windows desde el clon, activa WinRE con reagentc /enable. |
| Error_RecoveryConfigFailed | en | WinRE could not be configured on the clone. Windows will still start. Enable WinRE later with reagentc /enable. | WinRE could not be configured on the clone. Windows will still start. After starting Windows from the clone, enable WinRE with reagentc /enable. |

Ajustes del orquestador sobre la propuesta: Cancel_Confirm_Message conserva que el disco de origen no se modifica; Error_ShrinkInsufficient conserva el consejo de liberar espacio en el disco de origen; Disks_Strategy_Extend se acortó para la tarjeta del disco.
