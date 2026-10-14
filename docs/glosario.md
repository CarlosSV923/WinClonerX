# Glosario (lenguaje ubicuo)

El código usa exactamente estos nombres. El término en español es el concepto del dominio y el "Nombre en código" es su nombre en C#. Los contextos delimitados se describen en [data-model.md](../specs/001-system-disk-clone/data-model.md).

## Compartido

Valores usados por más de un contexto.

| Término | Nombre en código | Definición |
|---------|------------------|------------|
| Origen | `Source` | Disco del sistema del que se clona. Solo se modifica en la reducción aprobada y en su restauración. |
| Destino | `Target` | Disco elegido por la persona para recibir el clon. Se borra por completo al ejecutarse el plan. |
| Tamaño en bytes | `ByteSize` | Cantidad de bytes, siempre mayor o igual que cero, con formato en GiB y GB. |
| Tamaño de sector | `SectorSize` | Tamaño de sector lógico o físico de un disco. En la versión 1 solo puede valer 512 o 4096 bytes. |
| Identificador de disco | `DiskId` | Número de disco del sistema más el GUID de disco. Con GUID, la identidad es el GUID y el número se ignora. Sin GUID (RAW o MBR, `Guid.Empty`), dos identificadores son iguales solo si tienen el mismo número; uno con GUID nunca es igual a uno sin GUID. |
| Identificador de partición GPT | `PartitionGuid` | GUID único de una partición GPT. No puede estar vacío. |
| Tipo de partición GPT | `GptPartitionType` | GUID que identifica el tipo de una partición GPT. Las constantes conocidas son ESP, MSR, datos básicos, recuperación, LDM y Spaces. |
| Extensión | `Extent` | Tramo contiguo de un disco, definido por su desplazamiento y su longitud en bytes. El desplazamiento está alineado al sector y la longitud es mayor que cero. |
| Huella del inventario | `InventoryFingerprint` | Hash de la identidad y el diseño de los discos de origen y destino. Dos huellas son iguales si y solo si no ha cambiado nada relevante; si cambia, el plan se invalida. |

## Inventario

| Término | Nombre en código | Definición |
|---------|------------------|------------|
| Disco | `Disk` | Dispositivo de almacenamiento físico, descrito por su identificador, modelo, número de serie, capacidad, tamaño de sector, tipo de conexión y particiones. |
| Disco del sistema | `Disk.IsSystem` | Disco que contiene el Windows en ejecución o la partición EFI en uso. |
| Inventario de discos | `DiskInventory` | Imagen del estado de todos los discos en un instante dado, usada para planificar y para detectar cambios. Debe haber exactamente un disco del sistema; si no, el inventario es no soportado. |
| Firmware | `FirmwareType` | Tipo de arranque del equipo: `Uefi`, `Bios` o `Unknown`. En la versión 1 solo se soporta UEFI. |
| Estilo de partición | `PartitionStyle` | Esquema de tabla de particiones de un disco: `Gpt`, `Mbr` o `Raw`. El origen debe ser `Gpt`. |
| Tipo de conexión | `BusKind` | Bus por el que está conectado un disco: `Nvme`, `Sata`, `Sas`, `Usb`, `FileBackedVirtual`, `Spaces`, `Raid` u `Other`. |
| Partición | `Partition` | Región de un disco con número, posición (`Extent`), tipo GPT, identificador y rol propios. Puede tener un volumen con su sistema de archivos. |
| Rol de partición | `PartitionRole` | Función de una partición, derivada de su tipo: `EfiSystem`, `MsReserved`, `Windows`, `Recovery`, `Data` o `Unsupported`. |
| Volumen | `Volume` | Sistema de archivos de una partición, con su capacidad, espacio ocupado y cifrado. No existe en las particiones MSR ni en las particiones sin sistema de archivos. |
| Volumen de Windows | `Volume.IsWindowsVolume` | Volumen que contiene el Windows en ejecución. |
| Sistema de archivos | `FileSystemKind` | Tipo de sistema de archivos de un volumen: `Ntfs`, `Fat32`, `ExFat`, `ReFs`, `Raw` u `Other`. |
| Cifrado | `EncryptionState` | Estado de cifrado de un volumen: `None`, `BitLockerOn`, `BitLockerSuspended` o `Unknown`. |
| Volumen en uso por otros | `Volume.InUseByOthers` | Indica si un volumen de un candidato a destino está en uso. Solo se evalúa en los candidatos a destino; `null` significa que no se ha evaluado. |

## Planificación

| Término | Nombre en código | Definición |
|---------|------------------|------------|
| Comprobación previa | `PreflightCheck` | Verificación que se hace antes de mostrar el plan, con un código, una gravedad y los datos necesarios para explicar su resultado. |
| Código de comprobación | `PreflightCode` | Identificador de una comprobación previa, por ejemplo `FirmwareNotUefi`, `TargetInUse` o `BitLockerOn`. |
| Gravedad | `Severity` | Resultado de una comprobación: `Pass` (superada), `Warning` (requiere aceptación) o `Blocking` (impide el clonado). |
| Informe de comprobaciones | `PreflightReport` | Conjunto de comprobaciones previas de una planificación. Está bloqueado si alguna tiene gravedad `Blocking`. |
| Evaluador de comprobaciones | `PreflightEvaluator` | Servicio de dominio que, con el inventario y los hechos de la plataforma, produce el informe de comprobaciones y la elegibilidad de cada destino. |
| Hechos de la plataforma | `PlatformFacts` | Datos del entorno (BitLocker por volumen, batería, disponibilidad de instantáneas, espacio libre para ellas, reducción mínima y si cada destino está en uso) que la capa de aplicación obtiene y entrega al dominio. Un destino ausente en los hechos se trata como en uso. |
| Elegibilidad del destino | `TargetEligibility` | Resultado de evaluar un disco distinto del sistema como destino: si es apto, los motivos si no lo es y la estrategia de tamaño. |
| Espacio que falta | `Shortfall` | Cantidad de bytes que faltan para que los datos ocupados y el margen quepan en un destino no apto. |
| Estrategia de tamaño | `SizeStrategy` | Forma de ajustar el origen al destino: `SameOrLarger` (el destino es igual o mayor) o `ShrinkSource` (hay que reducir la partición de Windows del origen). |
| Margen de capacidad | `CapacityPolicy` | Espacio libre que se exige en el destino sobre los datos ocupados: el mayor entre el 10 % de lo ocupado y 10 GiB. Es configurable para las pruebas. |
| Calculadora del diseño de destino | `TargetLayoutCalculator` | Función pura que calcula las extensiones de las particiones en el destino a partir de las del origen, la estrategia y el margen. |
| Planificador de clonado | `ClonePlanner` | Servicio de dominio que coordina las comprobaciones y el cálculo del diseño, genera los identificadores nuevos y construye el plan. |
| Plan de clonado | `ClonePlan` | Decisión sobre cómo se clonará el origen en el destino, calculada a partir de un inventario concreto. Solo pasa a confirmado si se cumplen las confirmaciones exigidas. |
| Estado del plan | `PlanStatus` | Situación de un plan: `Draft` (en revisión), `Confirmed` (listo para ejecutar) o `Invalidated` (los discos cambiaron y hay que volver a planificar). |
| Correspondencia de partición | `PartitionMapping` | Relación entre una partición de origen y su extensión, identificador nuevo y método de copia en el destino. Hay una por cada partición de origen. |
| Extensión de destino | `TargetExtent` | Extensión que ocupará una partición en el disco de destino. |
| Método de copia | `CopyMethod` | Forma de copiar una partición: `UsedClustersFromSnapshot` (NTFS, solo los datos ocupados) o `RawFull` (EFI, MSR y el resto, completa). |
| Redimensionado | `ResizeKind` (propiedad `Resize`) | Ajuste de tamaño de una partición en el destino: `None` o `GrowFileSystem` (ampliar el sistema de archivos al espacio adicional). Solo aplica a la partición de Windows: cuando el destino es mayor, o cuando hay reducción y el espacio sobrante supera el tamaño reducido. |
| Reducción del origen | `SourceShrinkStep` | Paso del plan que reduce la partición de Windows del origen antes del clonado. Guarda el tamaño original, el tamaño reducido y el mínimo que admite Windows. |
| Confirmación del destino | `TargetConfirmation` | Confirmación escrita por la persona que identifica el disco que se borrará con los 4 últimos caracteres de su número de serie o, si no tiene, de su GUID. |
| Confirmación de reducción | `ShrinkConfirmation` | Confirmación explícita y separada de la del destino que autoriza reducir la partición de Windows del origen al tamaño propuesto. |
| Restauración del origen | `RestoreSource` (fase y compensación) | Devuelve la partición de Windows del origen a su tamaño original. Si falla en el camino feliz, el clon sigue siendo válido: la ejecución termina `Completed` con `SourceRestored = false` (`CloneExecution.RecordRestoreFailure`). Si falla como compensación, queda registrado sin ocultar el error original. |
| Modo de verificación | `VerificationMode` | Verificación que se hará al terminar: `Structural` (siempre) o `StructuralAndFull` (añade la comparación completa de datos, opcional y desactivada por defecto). |
| Estimación | `CloneEstimate` | Bytes que se copiarán y duración prevista del clonado. |
| Identificador de plan | `PlanId` | GUID que identifica un plan de clonado. No puede estar vacío. |
| Avisos aceptados | `AcceptedWarnings` | Códigos de aviso que la persona ha aceptado en un plan en borrador. Confirmar exige que estén todos. |
| Sufijo esperado | `TargetConfirmation.ExpectedSuffix` | Los 4 últimos caracteres del número de serie del destino en mayúsculas o, si no tiene, los 4 últimos del GUID de disco. Un número de serie vacío o formado solo por ceros (algunos puentes USB) usa el GUID (`TargetConfirmation.UsesGuidFallback`). Es lo que la persona debe escribir para confirmar. |
| Espacio requerido | `CapacityPolicy.Required` | Lo ocupado más el margen: lo mínimo que debe tener la partición de Windows del destino. |
| Área utilizable | `TargetLayoutCalculator` (primer y último byte) | Espacio del destino donde pueden colocarse las particiones: empieza en el primer byte utilizable y termina antes de las entradas y la cabecera GPT de respaldo. |
| Diseño del destino | `TargetLayout` | Tabla GPT completa que se escribirá en el destino: GUID de disco nuevo, tamaño y sector del disco y una entrada por partición. Lo produce `ClonePlan.ToTargetLayout`. |
| Entrada del diseño | `TargetLayoutEntry` | Partición del diseño del destino: tipo, identificador nuevo, extensión y, copiados del origen, atributos y nombre GPT. |

## Ejecución

| Término | Nombre en código | Definición |
|---------|------------------|------------|
| Ejecución de clonado | `CloneExecution` | Aplicación de un plan confirmado: avanza por sus fases, registra el progreso y los fallos y genera el informe final. |
| Estado de ejecución | `ExecutionState` | Situación de una ejecución: `Pending`, `Running`, `Cancelling`, `Cancelled`, `Failing`, `Failed` o `Completed`. |
| Fase | `ExecutionPhase` | Paso de una ejecución, en este orden: `Revalidate`, `ShrinkSource`, `Snapshot`, `PrepareTarget`, `CopyData`, `CommitLayout`, `ExpandFileSystem`, `RepairBoot`, `StructuralVerification`, `FullVerification`, `Cleanup` y `RestoreSource`. `ShrinkSource`, `ExpandFileSystem`, `FullVerification` y `RestoreSource` son condicionales. `Done` no es una fase planificada: marca una ejecución completada. |
| Compromiso del diseño | `CommitLayout` | Fase en la que se escribe la tabla de particiones del destino, que es el último paso que hace visible el diseño. Antes de ella el destino no puede arrancar, y si se cancela después, el diseño se borra. |
| Instantánea | `ISnapshotSet` | Copia coherente de los volúmenes del origen, obtenida con VSS al iniciar la ejecución, de la que se leen los datos del clon mientras Windows sigue en uso. Se elimina al terminar. |
| Compensación | `CompensationKind` (resultado: `CompensationRecord`, lista `Compensations`) | Acción que deshace un efecto parcial cuando la ejecución se cancela o falla, y su resultado. Se ejecutan en orden fijo: borrar el diseño del destino si se llegó a `CommitLayout`, borrar la instantánea, retirar los montajes temporales, restaurar el tamaño del origen si se redujo y permitir de nuevo la suspensión. Solo aplican las que corresponden a las fases iniciadas. |
| Identificador de ejecución | `ExecutionId` | GUID que identifica una ejecución de clonado. No puede estar vacío. |
| Cancelación pendiente | `CancelPending` (interno) | Cancelación pedida durante `CommitLayout`, que no se puede interrumpir. La ejecución sigue en marcha y pasa a `Cancelling` al completarse esa fase. |
| Fases planificadas | `PlannedPhases` | Fases que una ejecución va a recorrer, en orden. Las condicionales solo aparecen si aplican al plan. |
| Invariante de origen | `CloneExecution.PhaseMayModifySource` | Regla de que el disco de origen solo se modifica en `ShrinkSource` y `RestoreSource`. |
| Desenlace | `CloneOutcome` | Resultado final de una ejecución: `Completed`, `Cancelled` o `Failed`. Debe corresponder al estado final de la ejecución. |
| Fallo de ejecución | `ExecutionFailure` | Motivo por el que una ejecución falla: código, fase en la que ocurrió y detalle técnico. |
| Progreso | `CloneProgress` | Avance medido de la copia: bytes copiados, bytes totales, rendimiento y tiempo restante estimado (`Eta`), cuando existe. |
| Verificación estructural | `Structural` | Comprobación que se hace siempre al terminar: la tabla de particiones coincide con el plan, cada volumen se puede leer y la configuración de arranque está presente. |
| Verificación completa | `Full` | Comparación opcional, desactivada por defecto, del contenido copiado con el del origen, que informa de cualquier diferencia. |
| Resultado de verificación | `VerificationResult` | Resultado de una verificación estructural o completa. Lo usa el informe de resultado. |
| Registro de operación | `LogFile` | Archivo con las fases, el resultado, la duración y los errores de una ejecución. Puede exportarse. |
| Evento de dominio | `ExecutionStarted`, `PhaseStarted`, `PhaseCompleted`, `ProgressReported`, `CompensationCompleted`, `ExecutionCompleted`, `ExecutionCancelled`, `ExecutionFailed` | Hecho ocurrido durante una ejecución. Se registra en el log y alimenta la interfaz. |
| Informe de resultado | `CloneResultReport` | Resultado final de una ejecución: desenlace (`Completed`, `Cancelled` o `Failed`), verificaciones, estado del origen, entorno de recuperación, duración, registro y pasos siguientes. |
| Entorno de recuperación | `RecoveryEnvironmentState` | Estado del entorno de recuperación del clon: `Configured` (configurado) o `Warning` (aviso que no impide arrancar). |
| Pasos siguientes | `NextStepCode` | Código de la acción que la persona debe hacer tras el clonado, por ejemplo intercambiar los discos o elegir el arranque. |

## Reparación de arranque

| Término | Nombre en código | Definición |
|---------|------------------|------------|
| Especificación de reparación de arranque | `BootRepairSpec` | Descripción de los ajustes que el clon necesita para arrancar, derivada del plan: partición de Windows nueva, partición EFI, partición de recuperación si existe y los pasos a ejecutar. |
| Partición de Windows del clon | `WindowsPartition` | Partición de Windows del disco nuevo, identificada por su `PartitionGuid`. |
| Partición EFI del clon | `EspPartition` | Partición de sistema EFI del disco nuevo, desde la que arranca el firmware. |
| Partición de recuperación del clon | `RecoveryPartition` | Partición de recuperación del disco nuevo, si el origen la tenía. |
| Reescritura de MountedDevices | `MountedDevicesRewrite` | Ajuste que mantiene el volumen de Windows del clon como `C:` con su nuevo identificador de partición y elimina las entradas que apuntan a identificadores antiguos. |
| Pasos de reparación | `BootRepairStep` (propiedad `Steps`) | Acciones de la reparación de arranque, en orden: `RewriteMountedDevices`, `RebuildBcd` y `ReconfigureWinRe`. Este último solo si el origen tiene partición de recuperación. |
| Valor GPT de MountedDevices | `MountedDevicesRewrite.GptValue` | Valor de 24 bytes que identifica una partición en `MountedDevices`: el prefijo ASCII `DMIO:ID:` seguido del GUID de la partición en formato binario. |
| Valor obsoleto | `MountedDevicesRewrite.IsStale` | Valor de `MountedDevices` que apunta a una partición del origen y debe eliminarse del clon. |
| Reconstrucción de la BCD | `RebuildBcd` | Paso que crea la configuración de arranque del clon apuntando a su propio volumen de Windows. |
| Reconfiguración de WinRE | `ReconfigureWinRe` | Paso que configura el entorno de recuperación del clon para que use su partición de Windows y su partición de recuperación. |

## Reglas de uso

- Los términos nuevos se añaden a este glosario antes de usarse en el código.
- Si un término se renombra, el cambio se hace aquí y en el código en el mismo commit.
