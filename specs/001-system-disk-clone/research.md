# Research: Clonado del disco del sistema

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Fecha**: 2026-10-13

Cada decisión tiene el formato Decisión / Motivo / Alternativas. Las marcadas **(spike)** tienen
incertidumbre técnica residual y se validan con una prueba de concepto. S1 y S3 se hacen sobre VHDX en
la fase 2 de `tasks.md`. S2 necesita el motor de clonado completo, así que es un punto de control al
final de la parte técnica de la US1, antes de la interfaz. Si un spike falla, se aplica la alternativa
indicada.

Fuentes de Microsoft consultadas el 2026-10-13:
- [BCDBoot Command-Line Options](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/bcdboot-command-line-options-techref-di)
- [FSCTL_SHRINK_VOLUME](https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_shrink_volume)

---

## R1. Copia coherente del sistema en caliente

- **Decisión**: Volume Shadow Copy Service (VSS) en contexto *backup*, con todos los volúmenes NTFS del
  disco de origen en un único *snapshot set* coherente. Se accede por COM con interop propia generada
  en compilación (`[GeneratedComInterface]` + `LibraryImport` de `CreateVssBackupComponentsInternal`
  en `vssapi.dll`). Las instantáneas se leen desde su dispositivo
  (`\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN`) y se eliminan siempre al terminar.
- **Motivo**: es el mecanismo oficial de Windows para copias coherentes y hace participar a los
  *writers* (registro, bases de datos). La interop propia evita dependencias de terceros, es compatible
  con .NET 10 y se puede probar con fakes detrás del puerto `ISnapshotService`.
- **Alternativas**:
  - AlphaVSS: dependencia externa con un mantenimiento incierto en .NET moderno.
  - WMI `Win32_ShadowCopy.Create`: no controla los writers ni el contexto.
  - Driver propio de captura: prohibido por la constitución (principio V).
- **Notas**:
  - VSS no instantanea FAT32. La partición EFI se copia en bruto. Es pequeña y estática, y la app
    vuelve a escribir su contenido con `bcdboot` al final (R7).
  - La partición MSR no tiene sistema de archivos y se copia en bruto.
  - Los archivos excluidos por `FilesNotToSnapshot` (pagefile, hiberfil) no se copian. Windows los
    recrea al arrancar, y eso además reduce los datos a copiar.
  - Hay que comprobar que el origen tiene espacio libre suficiente para el área de diferencias de VSS.
    Esa comprobación forma parte de las comprobaciones previas.

## R2. Copiar solo los datos ocupados

- **Decisión**: leer el mapa de clústeres de cada volumen NTFS **de la instantánea** con
  `FSCTL_GET_VOLUME_BITMAP` (el tamaño de clúster sale de `FSCTL_GET_NTFS_VOLUME_DATA`), unir los
  clústeres ocupados contiguos en tramos y copiarlos. Además se copian siempre:
  - El sector 0 y la región final del volumen que queda después del último clúster completo, donde está
    el sector de arranque de respaldo de NTFS. Esa región no aparece en el mapa de clústeres.
  - Las particiones sin sistema de archivos reconocido (EFI, MSR y datos no NTFS), completas.
- **Motivo**: es lo que pide FR-016 y SC-003. El mapa de la instantánea coincide exactamente con los
  datos coherentes que se copian.
- **Lección del spike S3 (2026-10-13)**: la región final se calcula con la **longitud real del
  dispositivo de origen** (`IOCTL_DISK_GET_LENGTH_INFO`), no solo con `NumberSectors`. El dispositivo de una
  instantánea, sobre todo el de un volumen BitLocker, puede terminar antes. Además, el sector de arranque
  de respaldo de NTFS está en el desplazamiento `NumberSectors × BytesPerSector`, que es el último sector
  de la partición y queda fuera del volumen NTFS. Si no se puede leer del origen, se reconstruye a partir
  del sector de arranque primario, porque NTFS exige que sean idénticos. Este cálculo debe ser una función
  pura con pruebas unitarias en el adaptador de la US1 (`SnapshotVolumeSource`).
- **Lección 2 del spike S3**: el manejador del volumen de origen necesita
  **`FSCTL_ALLOW_EXTENDED_DASD_IO`** (0x00090083) antes de leer la región final y el sector de respaldo.
  NTFS aplica comprobaciones de límites en un volumen montado y, sin esa llamada, la lectura más allá del
  último clúster termina antes de tiempo. `SnapshotVolumeSource` debe hacer esta llamada al abrir el
  dispositivo de la instantánea.
- **Decisión de implementación (T041)**: con DASD habilitado, el sector de respaldo es legible, como
  demostró S3. Si aun así queda fuera de la longitud del dispositivo, `VolumeAllocationPlanner` **lanza una
  excepción y la ejecución aborta**. No se reconstruye a partir del primario: se prefiere no clonar antes
  que fabricar datos.
- **Alternativas**:
  - Copia sector a sector completa: incumple FR-016.
  - Copia a nivel de archivos: obligaría a reconstruir NTFS, ACL, enlaces y flujos alternativos, y es
    mucho más complejo.

## R3. E/S en bruto y rendimiento

- **Decisión**:
  - `CreateFile` sobre `\\.\PhysicalDriveN` (destino) y sobre el dispositivo de la instantánea (origen),
    con `FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH`.
  - Búferes alineados (`NativeMemory.AlignedAlloc`, múltiplo del sector físico) de 4 MiB.
  - Una tubería de lectura y escritura con `System.Threading.Channels`, con 2 a 4 búferes en vuelo.
- **Motivo**: sin la caché del sistema, el rendimiento es predecible y los datos quedan escritos de
  verdad al recibir la confirmación. La tubería solapa lectura y escritura para acercarse a la velocidad
  del disco más lento.
- **Alternativas**: E/S con caché (contamina la memoria y la confirmación de escritura no es fiable) o
  una sola hebra secuencial (más lenta).
- **Errores**: cualquier error de lectura en el origen o de escritura en el destino aborta la ejecución
  (FR-024, US3). No se reintenta con datos parciales.

## R4. Preparación del destino y "compromiso" atómico **(spike S1)**

- **Decisión**: patrón *la tabla de particiones es lo último que se escribe*:
  1. Desmontar y bloquear los volúmenes existentes del destino (`FSCTL_LOCK_VOLUME` +
     `FSCTL_DISMOUNT_VOLUME`). Si alguno no se puede bloquear, el destino está en uso y se aborta.
  2. Poner el disco **fuera de línea de forma no persistente** (`IOCTL_DISK_SET_DISK_ATTRIBUTES` con
     `DISK_ATTRIBUTE_OFFLINE`).
  3. Borrar los primeros y los últimos MiB (MBR y GPT primaria y de respaldo) para invalidar cualquier
     contenido anterior.
  4. Copiar los datos de cada partición en su desplazamiento final, escribiendo en bruto en el disco.
  5. **Compromiso**: escribir el MBR protector y la GPT primaria y de respaldo. La serialización es
     propia (CRC32 según la especificación UEFI) y se prueba con tests unitarios.
  6. Volver a poner el disco en línea y llamar a `IOCTL_DISK_UPDATE_PROPERTIES`. Windows descubre los
     volúmenes ya completos.
- **Motivo**:
  - Mientras la copia está en curso, no hay volúmenes RAW montados, así que Windows no muestra avisos de
    "formatear disco" ni asigna letras.
  - Un clon interrumpido antes del paso 5 no tiene tabla de particiones: aparece como "no inicializado"
    y es imposible arrancar desde él por error (FR-024).
  - Si se falla después del paso 5, se borra la tabla de particiones (`IOCTL_DISK_DELETE_DRIVE_LAYOUT`).
    Así el destino siempre queda "completo y verificado" o "no inicializado".
- **Lo que valida el spike S1**: (a) que Windows permite escribir en bruto en un disco fuera de línea;
  (b) que la GPT escrita a mano se reconoce después de `UPDATE_PROPERTIES`.
- **Resultado S1 (2026-10-13, VHDX 256 MiB, 512e, Windows 11 build 26300)**: ✅ **ruta principal confirmada**.
  - Con el disco fuera de línea (no persistente), todas las escrituras en bruto se completaron: patrón de
    datos, MBR protector, cabeceras y entradas GPT primarias y de respaldo.
  - Al volver a ponerlo en línea, `IOCTL_DISK_UPDATE_PROPERTIES` + `GET_DRIVE_LAYOUT_EX` devolvieron la GPT
    escrita a mano con su GUID de disco y la partición con su tipo, desplazamiento y tamaño exactos.
  - Los datos releídos coinciden byte a byte.
  - No hace falta la alternativa.
- **Alternativa si S1 falla**: crear el diseño con `IOCTL_DISK_CREATE_DISK` +
  `IOCTL_DISK_SET_DRIVE_LAYOUT_EX`, con el atributo GPT *no default drive letter* activado en todas las
  particiones durante la copia, y escribir en cada volumen bloqueado y desmontado. El "compromiso" pasa a
  ser restaurar los atributos y tipos definitivos. Si se falla, se aplica `DELETE_DRIVE_LAYOUT`.

## R5. Identificadores únicos y letra de unidad del clon

- **Decisión**:
  - Se genera un **GUID de disco nuevo** y un **GUID único nuevo para cada partición**. Se conservan los
    tipos, los atributos y los nombres GPT.
  - Para que el clon arranque con su volumen de Windows como `C:`, se edita el registro **fuera de línea**
    del clon: se carga `Windows\System32\config\SYSTEM` del destino con `RegLoadKey` (privilegios
    `SeBackupPrivilege` y `SeRestorePrivilege`), se reescribe `MountedDevices\\DosDevices\C:` con el
    formato GPT (`"DMIO:ID:"` + GUID único de la partición, 24 bytes) y se descarga con `RegUnLoadKey`.
  - Se eliminan las entradas `\??\Volume{...}` que apunten a GUID antiguos.
- **Motivo**: FR-017 exige que ambos discos puedan estar conectados a la vez. Con GUID duplicados,
  Windows pondría uno de los dos fuera de línea. Pero si se cambia el GUID de la partición de Windows sin
  corregir `MountedDevices`, el clon puede arrancar con otra letra (por ejemplo, `D:`) y romper perfiles
  y programas.
- **Alternativas**:
  - Conservar los GUID (clon idéntico): incumple FR-017 y es peligroso con los dos discos conectados.
  - Borrar `MountedDevices` entero: Windows reasigna las letras de forma no determinista.
  - `offreg.dll`: solo se distribuye con el WDK y no viene con el sistema.

## R6. Tamaños: destino mayor y destino menor

- **Decisión (destino mayor)**: el diseño de destino mantiene el orden de las particiones, alineadas a
  1 MiB. Las particiones de detrás de la de Windows (normalmente la de recuperación) se mueven al final
  del disco con su tamaño original. La partición de Windows se crea con el tamaño final y, después del
  "compromiso", su sistema de archivos se amplía con `FSCTL_EXTEND_VOLUME`. NTFS no depende de la
  posición absoluta, así que mover una partición es copiar sus datos en otro desplazamiento.
- **Decisión (destino menor)**: antes de crear la instantánea, la partición de Windows **del origen** se
  reduce con la **Storage Management API** (`MSFT_Partition.GetSupportedSize` para saber el mínimo
  posible y `MSFT_Partition.Resize` para reducir), mediante `System.Management` (WMI en
  `root\Microsoft\Windows\Storage`). Al terminar, sea cual sea el resultado, se restaura con
  `MSFT_Partition.Resize(tamaño original)` (FR-021a). La restauración se registra como paso de
  compensación del plan.
- **Motivo**: según la documentación de Microsoft, `FSCTL_SHRINK_VOLUME` obliga a quien la llama a
  reubicar cada archivo con `FSCTL_MOVE_FILE` y luego a encoger la partición con
  `IOCTL_DISK_GROW_PARTITION` negativo. Eso equivale a escribir un desfragmentador. `MSFT_Partition.Resize`
  es la API pública que ya hace todo eso (la usan `Resize-Partition` y el Administrador de discos), y es
  "la función nativa de Windows" decidida en `docs/decisiones-iniciales.md`.
- **Alternativas**:
  - `FSCTL_SHRINK_VOLUME` + `FSCTL_MOVE_FILE` propios: mucho más complejo y arriesgado.
  - Reubicar NTFS por cuenta propia: descartado en la decisión 1.
  - VDS (`IVdsVolumeMF2`): en desuso y reemplazada por la Storage Management API.
- **Margen de capacidad**: 10 % de los datos ocupados, con un mínimo de 10 GiB (supuesto de la
  especificación). El tamaño reducido propuesto es el mayor entre el mínimo que admite Windows y lo que
  ocupan los datos más el margen.
- **Hallazgo en hardware real (2026-10-21)**: en una prueba en un equipo real, la aplicación se cerró mientras
  preparaba el destino (fase `PrepareTarget`) con el origen ya reducido. Las compensaciones solo se ejecutan dentro
  del proceso, así que nada restauró la partición de Windows y hubo que ejecutar `Resize-Partition` a mano. Un corte
  de energía o un proceso matado darían el mismo resultado.
- **Decisión (diario de restauración, FR-021a)**: el archivo `%ProgramData%\WinCopy\pending-source-restore.json`
  guarda la partición, su longitud original, el plan y el instante UTC. Se escribe de forma duradera (archivo
  temporal con `FileOptions.WriteThrough` y `Flush(true)`, luego reemplazo) justo antes de `ShrinkAsync`: si la
  escritura falla, la ejecución aborta con `ShrinkJournalFailed` y no se reduce nada. Se borra solo cuando el origen
  tiene su longitud original, en el camino feliz o en la compensación `RestoreSource`. Si la restauración falla, el
  diario se conserva. Al iniciarse, `RecoverPendingSourceRestore` lee el diario y lo valida **fail-closed** contra el
  inventario actual (la partición debe ser la de Windows del disco del sistema, la longitud original debe ser mayor
  que la actual y no superar `SizeMax`); solo entonces restaura. Ver la tarea T121 y
  [contracts/log-events.md](contracts/log-events.md).

## R7. Arranque y entorno de recuperación del clon

- **Decisión**:
  - Después del "compromiso", se montan temporalmente el volumen de Windows y la partición EFI del clon
    en letras libres (`SetVolumeMountPoint`), que se retiran al terminar.
  - Se ejecuta `bcdboot <W>:\Windows /s <S>: /f UEFI` desde `%WINDIR%\System32`. Así se crea una
    BCD nueva en la partición EFI del clon que apunta a su propio volumen de Windows. **Con `/s`, bcdboot
    no crea entradas en la NVRAM del firmware** (documentación oficial), así que no se toca el orden de
    arranque del equipo. El firmware usa la ruta por defecto `\EFI\Boot\bootx64.efi`.
  - Para el entorno de recuperación se ejecuta
    `reagentc /setreimage /path <R>:\Recovery\WindowsRE /target <W>:\Windows` sobre el clon fuera de línea
    **(spike S2)**.
- **Motivo**: son herramientas del sistema, documentadas, y no requieren conocer el formato interno de la
  BCD.
- **Alternativas**:
  - Copiar la BCD tal cual: sigue apuntando a las particiones del origen.
  - Editar la BCD con WMI `BcdStore`: es más código, para el mismo resultado.
- **Lo que valida el spike S2**: que el clon arranca en una VM Gen2 de Hyper-V y que
  `reagentc /info` dentro del clon muestra WinRE habilitado. **Alternativa**: si WinRE no queda
  configurado, la app lo informa como aviso en el resultado e indica cómo ejecutar `reagentc /enable` en
  el clon. No bloquea, porque Windows arranca igualmente.
- **Nota 2026-10-21 (hallazgo en hardware real)**: en un Windows 11 en español el clon arranca, pero el menú
  muestra dos entradas "Windows 11". La partición EFI se copia en bruto, así que su BCD ya trae la entrada del
  origen. `bcdboot` añade otra y deja la antigua con `device`/`osdevice` "unknown" en el clon. La nueva pierde
  además `hypervisorlaunchtype Auto` (Hyper-V, WSL2, VBS), `locale es-MX` (bcdboot usa en-US) y
  `recoverysequence`. Por eso `reagentc /info` muestra WinRE "Disabled" con el id de la entrada antigua, y el
  `reagentc /setreimage` fuera de línea no basta.
  - **Decisión**: si la partición EFI del clon trae un almacén BCD con cargador por defecto, se **reapunta en su
    sitio** con `bcdedit /store <S>:\EFI\Microsoft\Boot\BCD /set ...`. Se reapuntan el gestor de arranque, el cargador
    por defecto (`device` y `osdevice` a `<W>:`), su objeto de reanudación, `{memdiag}` si existe, y el cargador de
    WinRE (`ramdisk=[<R>:]...`). Así se conservan todas las entradas y sus ajustes, y no se duplican. Al final,
    `bcdedit /enum {default} /v` debe mostrar `partition=<W>:`. `bcdboot` solo se ejecuta si no hay almacén o no hay
    cargador por defecto. El id de WinRE no cambia, así que el `ReAgent.xml` sigue siendo válido.
  - Todas las llamadas a `bcdedit` llevan `/store <S>:\EFI\Microsoft\Boot\BCD` como primeros argumentos, y el ejecutor
    lo exige antes de arrancar el proceso.
- **Resultado de S2 (2026-10-20, hardware real)**: superado con `8467029`. El clon arranca sin menú, con un solo
  cargador que conserva `locale`, `recoverysequence` y `hypervisorlaunchtype`. `reagentc /info` muestra WinRE
  `Enabled` en la partición de recuperación del clon, y WinRE arranca. Windows queda en `C:`. Procedimiento y
  pruebas en `docs/spikes/s2-arranque-hardware-real.md`.

## R8. Inventario de discos y comprobaciones previas

- **Decisión**:
  - Inventario con la Storage Management API: `MSFT_Disk` (modelo, número de serie, `BusType`,
    `IsSystem`, `IsBoot`, `PartitionStyle`, `LogicalSectorSize`, `PhysicalSectorSize`, GUID),
    `MSFT_Partition` (tipo GPT, desplazamiento, tamaño, GUID) y `MSFT_Volume` (sistema de archivos,
    espacio usado).
  - Completado con `IOCTL_DISK_GET_DRIVE_LAYOUT_EX` para tener los GUID y atributos GPT exactos.
  - Detección de firmware con `GetFirmwareType` (kernel32).
  - Estado de BitLocker con `Win32_EncryptableVolume` (`root\cimv2\Security\MicrosoftVolumeEncryption`).
  - Batería con `GetSystemPowerStatus`.
  - Un destino se considera en uso si no se pueden bloquear sus volúmenes.
- **Motivo**: es una sola API pública de alto nivel para casi todo el inventario, y deja menos P/Invoke
  que mantener.
- **Huella del inventario**: un hash de (GUID de disco, número de serie, tamaño y diseño de particiones)
  de origen y destino. Se compara al empezar la ejecución y entre fases (FR-011). Además, la app escucha
  la eliminación de dispositivos (`WM_DEVICECHANGE`) para abortar en cuanto se desconecte un disco.
- **No soportados (bloqueantes)**: BIOS/MBR, discos dinámicos (particiones LDM), Espacios de
  almacenamiento, RAID, tamaño de sector lógico distinto y más de una instalación de Windows.

## R9. BitLocker

- **Decisión**: si el volumen de Windows (o cualquier volumen del origen) está protegido, se muestra un
  aviso que requiere aceptación (FR-014). La lectura se hace a través de la instantánea del volumen
  desbloqueado, que ofrece los datos descifrados. El clon queda como NTFS sin cifrar **(spike S3)**.
- **Lo que valida el spike S3**: clonar un VHDX con BitLocker activado (BitLocker funciona sobre VHDX
  con protector de contraseña) y comprobar que el clon se monta como NTFS válido y sin cifrar. Para que
  el spike no dependa de la interop VSS de la US1, la instantánea se crea con WMI
  `Win32_ShadowCopy.Create`. Lo que se valida es el comportamiento de la pila de volúmenes con BitLocker,
  que es el mismo con ambas vías.
- **Resultado S3 (2026-10-13, VHDX 512 MiB, BitLocker `FullyEncrypted`/`On`)**: ✅ **ruta principal
  confirmada**.
  - La instantánea del volumen cifrado se lee descifrada como NTFS: volumen, mapa de clústeres y datos.
  - Se copiaron solo los clústeres usados (62 MiB en 9 tramos), el sector 0, la región final y el sector
    de arranque de respaldo.
  - El destino se monta como **NTFS sin cifrar** (`FullyDecrypted`/`Off`) y los **20/20 archivos**
    coinciden con su SHA-256.
  - BitLocker sigue siendo un **aviso** (decisión 7), no un bloqueo.
  **Alternativa**: si el resultado no es válido, BitLocker pasa a ser bloqueante en la v1 y hay que
  suspender o descifrar antes. Eso requiere revisar la decisión 7 con la persona usuaria.

## R10. Energía, suspensión e instancia única

- **Decisión**:
  - Suspensión: `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)` mientras dura la
    ejecución.
  - Instancia única: mutex con nombre `Global\WinCopy.SingleInstance`. Si ya existe una instancia, se
    trae al frente su ventana (FR-031).
- **Motivo**: son APIs públicas y sencillas, y se liberan automáticamente si el proceso termina.

## R11. Interfaz (WPF) y estética

- **Decisión**:
  - WPF en .NET 10 con el **tema Fluent integrado** (`ThemeMode`) como base, más diccionarios de
    recursos propios con la paleta, los tipos de letra y los componentes de WinCopy.
  - MVVM con **CommunityToolkit.Mvvm** (MIT) y DI con **Microsoft.Extensions.Hosting**.
  - Componentes clave: barra de disco con particiones a escala y color por tipo, tarjetas de disco y
    asistente por pasos con indicador de progreso lateral.
  - Estética inspirada en 4DDiG (navegación lateral, área de contenido clara, acento azul, tarjetas
    redondeadas), con recursos y marca propios (principio V).
- **Alternativas**: WinUI 3 (descartado en la decisión 3) o librerías de temas de terceros
  (dependencias innecesarias ahora que .NET trae Fluent).

## R12. Localización y accesibilidad

- **Decisión**:
  - Archivos `Strings.resx` (inglés, neutro) y `Strings.es.resx`, con una extensión de marcado
    `{loc:Text Key}`.
  - La cultura sale de `CultureInfo.CurrentUICulture`, con inglés como respaldo.
  - Un test verifica que todas las claves existen en ambos idiomas (SC-008).
  - Un analizador o test impide literales de texto en XAML.
  - Accesibilidad: `AutomationProperties.Name` y `HelpText` en las tarjetas de disco y en la
    confirmación, navegación con Tab y teclas de flecha, y foco inicial definido. Se verifica con
    Accessibility Insights (FR-030).

## R13. Confirmación informada del borrado

- **Decisión**: la confirmación de FR-012 exige **escribir los 4 últimos caracteres del número de serie**
  del disco de destino, que se muestra junto al modelo, la capacidad y las particiones actuales. El
  dominio solo acepta un `ClonePlan` confirmado con un `TargetConfirmation` que coincida con el disco del
  plan.
- **Motivo**: evita confirmar con un solo clic (FR-012) y ayuda a cumplir SC-007 (identificar el disco
  correctamente). Lo usan herramientas de borrado seguro.
- **Alternativas**: escribir "BORRAR" (no demuestra que se identificó el disco) o una casilla más un
  botón (demasiado fácil de pasar por alto).

## R14. Registro y exportación

- **Decisión**:
  - **Serilog** (Apache-2.0) con formato JSON compacto (`Serilog.Formatting.Compact`), un archivo por
    ejecución en `%ProgramData%\WinCopy\logs\`.
  - Eventos estructurados con el esquema de [contracts/log-events.md](contracts/log-events.md).
  - La exportación copia el archivo de la ejecución, y opcionalmente el inventario en JSON, a la ruta
    que elija la persona (FR-026).
  - Sin telemetría ni red (FR-027). El proyecto no referencia ninguna librería HTTP.
- **Alternativas**: `Microsoft.Extensions.Logging` con un proveedor de archivo propio (más código para
  lo mismo).

## R15. Pruebas y seguridad de las pruebas

- **Decisión**:
  - **xUnit v3** (Apache-2.0) y **Shouldly** (BSD) para aserciones. Fakes escritos a mano para los
    puertos (sin librerías de mocks).
  - **NetArchTest.Rules** (MIT), solo en `WinCopy.ArchitectureTests`: verifica de forma declarativa que
    el dominio no depende de Windows ni WPF y que las capas solo dependen hacia dentro (principio III).
    Alternativa descartada: reflexión manual sobre las referencias de ensamblado (más código propio que
    mantener y menos expresivo).
  - Integración sobre **VHDX** creados con la API `virtdisk.dll` (`CreateVirtualDisk`,
    `AttachVirtualDisk`), sin depender del módulo de Hyper-V. Requiere ejecutar las pruebas como
    administrador.
  - **Protección contra discos físicos** (principio IV), en dos capas que no dependen de que la prueba
    esté bien escrita:
    1. `DiskWriteGuard` es el único punto de producción que abre manejadores de escritura en un disco.
       Admite restricciones globales del proceso que **solo pueden añadir prohibiciones**.
    2. El ensamblado de pruebas de integración registra con un `[ModuleInitializer]` una restricción
       que solo autoriza discos `BusType == FileBackedVirtual` (15) respaldados por un VHDX bajo
       `%TEMP%\WinCopyTests\`. Así, aunque una prueba cree un adaptador directamente con el número de un
       disco físico, la apertura falla antes de llamar a `CreateFile`.
  - Arranque: pruebas manuales o semiautomatizadas en VM Gen2 de Hyper-V (opcional) y validación final
    en el segundo disco físico con la lista de [quickstart.md](quickstart.md).
- **Alternativas**:
  - FluentAssertions: licencia comercial desde la v8.
  - Moq o NSubstitute: los fakes explícitos documentan mejor los puertos de DDD.

## R16. Instalador y publicación

- **Decisión**:
  - **WiX Toolset v6** con proyecto SDK (`WixToolset.Sdk`): MSI x64 por máquina, instalado en
    `Program Files\WinCopy`, con acceso directo en el menú Inicio y desinstalación limpia (FR-032).
  - La app se publica como *self-contained* para win-x64, sin necesidad de instalar el runtime de .NET.
  - El manifiesto de la app declara `requireAdministrator`.
  - Las versiones se publican sin firmar, con SHA-256 y GitHub Artifact Attestations (constitución
    v1.1.0).
- **Notas**: WiX es MS-RL. La *Open Source Maintenance Fee* de WiX aplica a proyectos con ingresos, no a
  este proyecto gratuito de portafolio. Hay que volver a evaluarlo si eso cambia.
- **Alternativas**: dependiente del framework (más ligero, pero exige el runtime y puede fallar en
  equipos sin él) o MSIX (descartado en la decisión 4).

## R17. Estimación de progreso

- **Decisión**:
  - El total de bytes se conoce antes de empezar: la suma de los tramos ocupados más las particiones en
    bruto.
  - El progreso se calcula con los bytes confirmados por escritura.
  - El tiempo restante se estima con una media móvil exponencial del rendimiento de los últimos 30 s, y
    se muestra a partir del 1 % o de 10 s de ejecución.
  - La duración inicial del plan se calcula con un rendimiento conservador por tipo de bus (para mostrar
    algo antes de empezar).
- **Motivo**: cumple FR-019 y SC-006 (±20 % desde el 10 %).

## R18. Verificación completa de los datos (FR-022a)

- **Decisión (2026-10-21)**: la comparación byte a byte es la fase `FullVerification`, después de `CopyData` y **antes de
  `CommitLayout`**, con el destino todavía fuera de línea y bloqueado. La tarea pedía tras `CommitLayout`; se cambia por lo
  que sigue.
- **Por qué antes de `CommitLayout`**: `CommitLayout` también pone el disco en línea (`BringOnline`) y libera los bloqueos.
  Con los volúmenes montados, NTFS puede escribir en ellos al montarlos (por ejemplo, el estado de `$Volume` y el registro
  de NTFS), y esas escrituras aparecerían como diferencias que no son del clonado. Mientras el disco está fuera de línea,
  ninguna escritura de Windows llega al destino. Además, si la comparación falla no hay diseño escrito que borrar.
- **Por qué antes de la ampliación y del arranque**: `ExpandFileSystem` reescribe el sector de arranque (`NumberSectors`), el
  mapa de bits de NTFS y el sector de arranque de respaldo al final del volumen. `RepairBoot` cambia `MountedDevices`, el BCD
  de la ESP y `ReAgent.xml`. Si la comparación fuera después, esos cambios aparecerían como diferencias esperables. Antes, el
  destino es exactamente lo que escribió la copia.
- **Qué se compara**: los mismos tramos ocupados que copió la copia (`CopiedRange`), leídos de nuevo de la misma instantánea
  (sigue abierta hasta `Cleanup`) y del destino con `ITargetDisk.ReadAsync` en su desplazamiento. Las particiones copiadas en
  bruto (EFI, MSR) se comparan completas, porque su asignación es el tramo entero.
- **Lectura y memoria**: `VolumeComparer` usa dos búferes alineados de 4 MiB por comparación, en fragmentos. Los errores se
  clasifican como la copia: origen con `SourceReadError` o la desconexión; destino con `IoErrorClassifier.ForTargetIo`.
- **Resultado**: `VerificationResult` con los bytes distintos totales y los primeros 10 desplazamientos del disco de destino.
  El detalle por partición (número de bytes y desplazamientos) va en `Issues`. Un byte distinto falla la ejecución con
  `VerificationFailed` y las compensaciones habituales; el informe lleva el resultado completo en `Full`.
- **Registro**: un `DiskOperation` `Read` por partición sobre el destino, y `FullVerificationCompleted` al terminar.
- **Estimación**: con la verificación completa, la duración estimada es la de la copia más una segunda pasada igual (aprox.
  el doble, FR-022a). El rendimiento inicial de la copia usa solo la duración de la copia.
- **Pendiente de comprobar en hardware real**: que el destino fuera de línea devuelve los mismos bytes que escribió la copia,
  y que la comparación de un clon real con BitLocker y con el origen en instantánea no da diferencias espurias.
