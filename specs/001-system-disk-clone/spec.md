# Feature Specification: Clonado del disco del sistema

**Feature Branch**: `001-system-disk-clone`

**Created**: 2026-10-13

**Status**: Draft

**Input**: User description: "Aplicación instalable para Windows cuya única funcionalidad es clonar el disco del sistema a otro disco (caso típico: mover Windows de un HDD a un SSD). Decisiones previas en `docs/decisiones-iniciales.md`."

## Clarifications

### Session 2026-10-13

- Q: Si el disco del sistema tiene particiones de datos además de las de Windows, ¿qué se clona? → A: El disco completo, con todas sus particiones. Solo la partición de Windows puede cambiar de tamaño.
- Q: Si se redujo la partición de Windows del origen, ¿qué se hace con ella al terminar? → A: Se vuelve a ampliar automáticamente a su tamaño original, sea cual sea el resultado del clonado.
- Q: ¿Qué verificación se hace al terminar? → A: Siempre una verificación estructural, más una verificación completa de los datos opcional y desactivada por defecto.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clonar el sistema a un disco de igual o mayor tamaño (Priority: P1)

Una persona quiere pasar su Windows a un disco nuevo, igual o más grande que el actual. Abre WinCopy y la aplicación detecta automáticamente el disco del sistema. La persona elige el disco de destino entre los disponibles y revisa un resumen de lo que va a ocurrir: qué particiones se copiarán, cómo quedarán en el disco nuevo y qué disco se borrará. Confirma y sigue el progreso hasta el final. El disco nuevo queda listo para arrancar Windows tal como estaba, con sus programas, archivos y configuración. Si el destino es más grande, el espacio adicional se asigna a la partición de Windows.

**Why this priority**: es el núcleo del producto. Por sí sola ya entrega el valor principal (migrar el sistema sin reinstalar) y es la base de todas las demás historias.

**Independent Test**: clonar un sistema de referencia a un disco de prueba más grande, quitar o desactivar el disco original y comprobar que Windows arranca desde el clon con los mismos archivos y programas, y que la partición de Windows ocupa el espacio adicional.

**Acceptance Scenarios**:

1. **Given** un equipo con Windows en UEFI/GPT y un segundo disco de mayor capacidad conectado, **When** la persona abre WinCopy, **Then** la aplicación muestra el disco del sistema como origen (no editable) y el segundo disco como destino seleccionable, con su modelo, número de serie, capacidad y contenido actual.
2. **Given** que la persona eligió un destino, **When** pasa a la revisión, **Then** ve el plan completo: particiones de origen, cómo quedarán en el destino con sus tamaños, aviso de que el destino se borrará por completo y tiempo estimado.
3. **Given** que la persona revisa el plan, **When** confirma explícitamente el borrado del disco identificado, **Then** comienza el clonado y se muestra el progreso real (porcentaje, datos copiados respecto al total y tiempo restante).
4. **Given** que el clonado terminó, **When** la aplicación verifica el resultado, **Then** informa del éxito e indica los pasos para empezar a usar el disco nuevo (por ejemplo, intercambiar los discos o elegir el arranque).
5. **Given** un clon terminado y el disco original retirado, **When** se enciende el equipo, **Then** Windows arranca desde el disco nuevo con los mismos usuarios, programas, archivos y activación.
6. **Given** un destino más grande que el origen, **When** termina el clonado, **Then** la partición de Windows del destino ocupa el espacio adicional, y las particiones de arranque y recuperación conservan su función.

---

### User Story 2 - Clonar a un disco más pequeño que el origen (Priority: P2)

Una persona tiene un HDD grande con poco espacio ocupado y quiere pasar a un SSD más pequeño, pero con capacidad suficiente para los datos. WinCopy detecta que el destino es más pequeño que el origen y calcula si los datos caben. Si caben, explica que antes de clonar hay que reducir la partición de Windows del disco original y pide una confirmación específica para esa modificación. Después clona igual que en la historia 1.

**Why this priority**: es el caso más habitual de migración de HDD a SSD, pero es más arriesgado porque modifica el disco de origen. Depende de que la historia 1 funcione.

**Independent Test**: preparar un sistema de referencia cuyo espacio usado quepa en un destino más pequeño, clonar y comprobar que el clon arranca y que el sistema original sigue arrancando con normalidad tras la reducción.

**Acceptance Scenarios**:

1. **Given** un destino más pequeño que el origen pero con capacidad suficiente para los datos ocupados y un margen de seguridad, **When** la persona lo elige, **Then** la aplicación indica que hay que reducir la partición de Windows del origen, muestra el tamaño final propuesto y pide una confirmación aparte de la de borrar el destino.
2. **Given** que la persona confirma la reducción, **When** Windows no puede reducir la partición lo suficiente (por ejemplo, por archivos que no se pueden mover), **Then** la aplicación aborta antes de tocar el destino, explica el motivo y sugiere qué se puede hacer.
3. **Given** un destino donde no caben los datos ocupados más el margen, **When** la persona intenta elegirlo, **Then** la aplicación lo marca como no apto y muestra cuánto espacio faltaría.
4. **Given** una reducción del origen terminada y el clonado completado, **When** la persona arranca desde el disco original, **Then** Windows funciona con normalidad y sin pérdida de datos.
5. **Given** una reducción del origen terminada, **When** el clonado termina de cualquier forma (éxito, cancelación o fallo), **Then** la aplicación vuelve a ampliar automáticamente la partición de Windows del origen a su tamaño original y lo confirma en el informe.
6. **Given** que la ampliación automática del origen no puede completarse, **When** termina el clonado, **Then** la aplicación lo informa de forma destacada, indica cuánto espacio quedó sin asignar y explica cómo recuperarlo con las herramientas de Windows. El sistema original sigue arrancando con normalidad.

---

### User Story 3 - Cancelación y fallos sin poner en riesgo el sistema (Priority: P3)

Una persona cancela el clonado a mitad, o se produce un fallo (se desconecta el disco, hay un error de lectura o escritura, se agota la batería). En cualquier caso, el sistema original queda intacto y utilizable. El destino queda claramente marcado como incompleto, y la persona recibe una explicación comprensible y el registro técnico para pedir ayuda.

**Why this priority**: la historia 1 necesita estas garantías para ser segura (principio I de la constitución), pero se pueden probar por separado inyectando fallos.

**Independent Test**: provocar cancelaciones y fallos simulados en distintas fases (antes de tocar el destino, durante la copia y durante la reparación del arranque) y comprobar que el origen sigue arrancando, que el destino no se considera válido y que el mensaje y el registro describen lo ocurrido.

**Acceptance Scenarios**:

1. **Given** un clonado en curso, **When** la persona pulsa Cancelar, **Then** la aplicación pide confirmación, detiene la operación de forma ordenada, informa de que el destino quedó incompleto y no puede arrancar, y deja el origen intacto.
2. **Given** un clonado en curso, **When** el disco de destino se desconecta, **Then** la aplicación detiene la operación, informa del problema en lenguaje claro y conserva el registro técnico.
3. **Given** un error de lectura en el origen, **When** ocurre durante la copia, **Then** la aplicación aborta (no genera un clon con datos corruptos sin avisar) y explica que el disco de origen podría tener problemas físicos.
4. **Given** cualquier finalización (éxito, cancelación o fallo), **When** la persona lo solicita, **Then** puede exportar el registro de la operación a un archivo.

---

### Edge Cases

- **Destino en uso**: si el disco de destino tiene volúmenes en uso (archivos abiertos, archivo de paginación o una instalación de Windows activa), la aplicación MUST impedir el clonado y explicar el motivo.
- **Cambio de discos**: si se conectan o desconectan discos entre la planificación y la ejecución, el plan MUST invalidarse y recalcularse.
- **Arquitectura no soportada**: si el sistema arranca en BIOS/MBR o el disco de origen es dinámico, RAID o de Espacios de almacenamiento, la aplicación MUST informar de que el caso no está soportado en esta versión y no ofrecer el clonado.
- **Tamaño de sector distinto**: si el tamaño de sector lógico del origen y del destino difiere (512 frente a 4096 bytes), el destino MUST marcarse como no compatible en esta versión.
- **BitLocker**: si el volumen de Windows está cifrado con BitLocker, la aplicación MUST avisar de que el clon quedará sin cifrar y explicar cómo reactivar el cifrado después. La persona debe aceptarlo para continuar.
- **Uso durante el clonado**: los cambios que la persona haga en el sistema después de iniciar el clonado no se incluirán en el clon. La aplicación MUST avisarlo antes de empezar.
- **Energía y suspensión**: en un portátil con batería, la aplicación MUST recomendar conectar el cargador antes de empezar. Durante el clonado, el equipo MUST NOT entrar en suspensión.
- **Destino con datos**: si el destino contiene particiones o datos, MUST mostrarse claramente que todo se borrará.
- **Disco del sistema como destino**: el disco del sistema MUST NOT aparecer nunca como destino seleccionable, sea cual sea su estado.
- **Particiones adicionales**: si el disco del sistema contiene particiones de datos además de las de Windows, se clona el disco completo con todas sus particiones. Si el destino es más pequeño, solo se reduce la partición de Windows; las demás deben caber con su tamaño. Si no caben, el destino se marca como no apto.
- **Sin destinos válidos**: si no hay ningún disco apto conectado, la aplicación MUST explicar qué se necesita (capacidad mínima y tipo de conexión) en lugar de mostrar una lista vacía.
- **Ejecuciones simultáneas**: MUST NOT poder ejecutarse dos clonados a la vez. Si ya hay una instancia de la aplicación abierta, se trae al frente.

## Requirements *(mandatory)*

### Functional Requirements

**Detección e inventario**

- **FR-001**: El sistema MUST identificar automáticamente el disco que contiene el Windows en ejecución y fijarlo como origen.
- **FR-002**: El sistema MUST listar los demás discos conectados con modelo, número de serie, capacidad, tipo de conexión (interno o USB) y particiones actuales.
- **FR-003**: El sistema MUST indicar para cada posible destino si es apto o no y, si no lo es, el motivo (capacidad insuficiente, en uso, tamaño de sector distinto, etc.).
- **FR-004**: El sistema MUST NOT permitir seleccionar como destino el disco del sistema ni ningún disco que contenga volúmenes del sistema en ejecución.

**Comprobaciones previas**

- **FR-005**: Antes de mostrar el plan, el sistema MUST verificar: arranque UEFI con disco GPT, capacidad suficiente del destino, compatibilidad del tamaño de sector, estado de BitLocker, disponibilidad del mecanismo de instantáneas del sistema y que el destino no esté en uso.
- **FR-006**: Si alguna comprobación falla o no puede completarse, el sistema MUST bloquear el clonado y explicar el motivo y la posible solución.

**Plan de clonado**

- **FR-007**: El sistema MUST calcular un plan de clonado sin modificar ningún disco y mostrarlo antes de ejecutar nada. El plan incluye las particiones de origen y destino con sus tamaños, si se reducirá el origen, si se ampliará la partición de Windows en el destino, los avisos y una estimación de duración.
- **FR-008**: Cuando el destino sea más grande, el plan MUST asignar el espacio adicional a la partición de Windows y mantener las particiones de arranque y recuperación funcionales.
- **FR-009**: Cuando el destino sea más pequeño que el origen pero los datos quepan con margen, el plan MUST incluir la reducción de la partición de Windows del origen y el tamaño final propuesto.
- **FR-010**: El sistema MUST rechazar destinos en los que no quepan los datos ocupados más un margen de seguridad, e indicar cuánto espacio falta.
- **FR-011**: Si el estado de los discos cambia entre el plan aprobado y la ejecución, el sistema MUST abortar y volver a planificar.

**Confirmaciones**

- **FR-012**: El sistema MUST exigir una confirmación explícita que identifique el disco que se borrará por modelo, número de serie y capacidad. No basta con un único clic sobre un botón genérico.
- **FR-013**: Si el plan incluye reducir el origen, el sistema MUST pedir una confirmación adicional y separada para esa modificación.
- **FR-014**: Si el volumen de Windows está cifrado con BitLocker, el sistema MUST pedir que la persona acepte de forma explícita que el clon quedará sin cifrar.

**Ejecución**

- **FR-015**: El sistema MUST clonar el sistema mientras Windows está en ejecución, a partir de una copia coherente del estado del sistema en el momento de iniciar, sin necesidad de reiniciar.
- **FR-016**: El sistema MUST copiar solo los datos ocupados de cada partición (no el espacio libre), de forma que la duración dependa de los datos usados y no de la capacidad del disco.
- **FR-017**: El sistema MUST asignar al disco clonado identificadores únicos, de modo que ambos discos puedan estar conectados al mismo equipo sin conflicto.
- **FR-018**: El sistema MUST dejar el destino capaz de arrancar Windows por sí solo, incluida la configuración de arranque y el entorno de recuperación.
- **FR-019**: El sistema MUST mostrar el progreso real: fase actual, porcentaje, datos copiados respecto al total y tiempo restante estimado.
- **FR-020**: El sistema MUST impedir que el equipo entre en suspensión durante el clonado.
- **FR-021**: El sistema MUST NOT modificar el disco de origen, salvo la reducción aprobada según FR-013 y su posterior restauración (FR-021a).
- **FR-021a**: Si se redujo el origen, el sistema MUST volver a ampliar su partición de Windows al tamaño original cuando termine el clonado, sea cual sea el resultado. Si no lo consigue, MUST informarlo de forma destacada con las instrucciones para recuperar el espacio.
- **FR-021b**: El sistema MUST clonar todas las particiones del disco de origen, incluidas las de datos. Solo la partición de Windows puede cambiar de tamaño; las demás conservan el suyo.

**Resultado y verificación**

- **FR-022**: Al terminar, el sistema MUST hacer siempre una verificación estructural del destino y mostrar su resultado. La verificación comprueba que la tabla de particiones coincide con el plan, que cada volumen se puede leer y que la configuración de arranque está presente.
- **FR-022a**: El sistema MUST ofrecer antes de iniciar una opción, desactivada por defecto, para verificar todos los datos. Esa verificación compara el contenido copiado en el destino con el de la copia coherente del origen e informa de cualquier diferencia. La interfaz MUST indicar que esta opción aproximadamente duplica la duración.
- **FR-023**: Tras un clonado correcto, el sistema MUST mostrar los pasos para empezar a usar el disco nuevo.
- **FR-024**: Si la operación se cancela o falla, el sistema MUST dejar el origen utilizable, marcar el destino como incompleto y explicar lo ocurrido en lenguaje claro.

**Registro y privacidad**

- **FR-025**: El sistema MUST registrar cada operación sobre los discos: fase, disco, resultado, duración y errores.
- **FR-026**: El sistema MUST permitir exportar el registro a un archivo elegido por la persona.
- **FR-027**: El sistema MUST NOT enviar ningún dato fuera del equipo.

**Experiencia de uso**

- **FR-028**: La interfaz MUST guiar el proceso como un asistente por pasos: origen y destino, revisión del plan, confirmación, progreso y resultado.
- **FR-029**: Todos los textos visibles MUST estar disponibles en español e inglés, y el idioma se elige según el de Windows (inglés como respaldo).
- **FR-030**: Los pasos de selección del destino y confirmación MUST poder completarse solo con el teclado y ser comprensibles con un lector de pantalla.
- **FR-031**: El sistema MUST impedir dos clonados simultáneos.
- **FR-032**: La aplicación MUST instalarse y desinstalarse con un instalador estándar de Windows, y la desinstalación MUST NOT dejar componentes activos en el sistema.

### Key Entities

- **Disco**: dispositivo de almacenamiento físico. Se describe por identificador, modelo, número de serie, capacidad, tamaño de sector, tipo de conexión y si es el disco del sistema.
- **Partición**: región de un disco con un tipo (arranque EFI, reservada, Windows, recuperación o datos), un tamaño, una posición y, si lo tiene, un volumen con su espacio ocupado.
- **Inventario de discos**: imagen del estado de todos los discos en un momento dado. Sirve para planificar y para detectar cambios.
- **Resultado de comprobación previa**: cada verificación con su estado (superada, aviso o bloqueante), el motivo y la acción sugerida.
- **Plan de clonado**: decisión inmutable sobre cómo se clonará. Contiene el origen, el destino, la correspondencia de cada partición de origen con su ubicación y tamaño en el destino, la reducción del origen si procede (y su restauración posterior), si se hará la verificación completa, los avisos aceptados y la estimación. Se calcula a partir de un inventario concreto.
- **Ejecución de clonado**: aplicación del plan en curso. Tiene un estado (pendiente, en curso, cancelada, fallida o completada), una fase actual, el progreso y los errores.
- **Informe de resultado**: resultado final con la verificación, la duración, los pasos siguientes y la referencia al registro.
- **Registro de operación**: secuencia con fecha y hora de las acciones realizadas sobre los discos, exportable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Una persona sin conocimientos técnicos llega desde abrir la aplicación hasta iniciar el clonado en menos de 3 minutos y con no más de 5 decisiones. Una "decisión" es cada pantalla en la que la persona tiene que elegir o confirmar algo. Aceptar varios avisos en la misma pantalla cuenta como una sola decisión.
- **SC-002**: El 100 % de los clones de la matriz de validación arrancan Windows al primer intento desde el disco nuevo, con los mismos usuarios, programas, archivos y activación que el original.
- **SC-003**: La duración depende de los datos ocupados y no de la capacidad. Clonar un sistema con 100 GB ocupados en un disco de 1 TB tarda como máximo un 20 % más que clonar 100 GB ocupados en un disco de 200 GB, con el mismo hardware.
- **SC-004**: En el 100 % de las pruebas (incluidas las que fuerzan errores de selección) no se escribe nunca en el disco del sistema ni en un disco distinto del confirmado.
- **SC-005**: Tras cualquier cancelación o fallo provocado en cualquier fase, el sistema original arranca y funciona con normalidad en el 100 % de los casos.
- **SC-006**: A partir del 10 % del progreso, el tiempo restante estimado no se desvía más de un ±20 % de la duración real.
- **SC-007**: En pruebas de uso, al menos el 90 % de los participantes identifica correctamente qué disco se borrará antes de confirmar.
- **SC-008**: El 100 % de los textos visibles está disponible en español y en inglés.
- **SC-009**: Con la verificación completa activada, el 100 % de las alteraciones introducidas a propósito en los datos del destino durante las pruebas se detectan y se informan.
- **SC-010**: En el 100 % de los clonados que requirieron reducir el origen, la partición de Windows del origen vuelve a su tamaño original o, si no es posible, la persona recibe instrucciones para recuperar el espacio.

## Assumptions

- El equipo arranca en modo UEFI y el disco de origen usa GPT. BIOS/MBR queda fuera de esta versión (constitución, principio VI).
- Sistemas compatibles: Windows 10 22H2 y Windows 11 de 64 bits, con una sola instalación de Windows en el disco de origen (sin arranque dual).
- La persona usuaria tiene permisos de administrador en el equipo.
- El destino puede estar conectado por USB (por ejemplo, un SSD en una carcasa externa). Se usa como soporte para instalarlo luego dentro del equipo; arrancar Windows de forma habitual desde USB no es un objetivo.
- El margen de seguridad para considerar que los datos caben es del 10 % del espacio ocupado, con un mínimo de 10 GiB. Se ajustará en la planificación si hace falta.
- La aplicación no cambia la configuración del firmware (orden de arranque). Solo indica a la persona cómo hacerlo.
- La reducción del origen usa la función del propio Windows y está sujeta a sus límites (archivos que no se pueden mover).
- Se considera que el disco de origen está sano. Ante errores de lectura, la aplicación aborta en lugar de intentar recuperar datos.
- Las decisiones de producto y tecnología de referencia están en `docs/decisiones-iniciales.md` y en la constitución v1.0.0.
