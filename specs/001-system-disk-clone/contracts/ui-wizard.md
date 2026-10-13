# Contrato: Asistente de clonado (interfaz)

Define los pasos, la información obligatoria y los controles de cada pantalla. Los ViewModels se prueban
contra este contrato. El diseño visual (paleta, tipografía, componentes) se concreta en la
implementación, inspirado en la estética de 4DDiG con recursos propios (principio V).

## Estructura general

- La ventana tiene una **barra lateral** con los pasos del asistente (el paso actual resaltado y los
  completados marcados) y un **área de contenido** a la derecha.
- La barra inferior tiene los botones **Atrás** y **Siguiente**/acción principal. **Siguiente** solo se
  habilita cuando el paso es válido.
- Todos los textos salen de los recursos `.resx` (español e inglés). No hay textos en el XAML (FR-029).
- La navegación es posible solo con el teclado y cada control interactivo tiene un nombre accesible
  (FR-030).

## Paso 1: Discos (origen y destino)

| Elemento | Contenido | Requisito |
|----------|-----------|-----------|
| Tarjeta de origen | "Disco del sistema", modelo, capacidad, barra de particiones con espacio usado. No se puede seleccionar | FR-001 |
| Lista de destinos | Una tarjeta por disco no del sistema: modelo, número de serie, capacidad, bus (Interno/USB), barra de particiones actuales y estado *Apto* / *No apto* con su motivo | FR-002, FR-003 |
| Estrategia | En los aptos, una indicación: "Se ampliará Windows" o "Hay que reducir Windows en el disco original" | FR-008, FR-009 |
| Destinos no aptos | Visibles, pero no seleccionables, con el motivo y lo que falta (p. ej. "Faltan 23 GB") | FR-010 |
| Sin destinos | Estado vacío explicativo: capacidad mínima necesaria y cómo conectar un disco. Botón **Actualizar** | Edge case |
| Bloqueos globales | Si el sistema no está soportado (BIOS/MBR, discos dinámicos…), se muestra un panel con la explicación en lugar de la lista | FR-005, FR-006 |

El disco del sistema **nunca** aparece en la lista de destinos.

## Paso 2: Revisión del plan

| Elemento | Contenido | Requisito |
|----------|-----------|-----------|
| Comparativa | Dos barras alineadas, "Antes (origen)" y "Después (destino)", con cada partición, su tamaño y su cambio (+/−) | FR-007 |
| Resumen | Datos a copiar, duración estimada y si se reducirá el origen | FR-007 |
| Avisos | Uno por aviso (BitLocker, batería, cambios después de iniciar…), con una casilla "Entiendo" obligatoria | FR-014, edge cases |
| Opción | Casilla "Verificar todos los datos (aprox. duplica la duración)", desactivada por defecto | FR-022a |

## Paso 3: Confirmación

| Elemento | Contenido | Requisito |
|----------|-----------|-----------|
| Disco a borrar | En grande: modelo, número de serie completo, capacidad y particiones actuales. Texto: "Se borrará TODO el contenido de este disco" | FR-012 |
| Campo de verificación | "Escribe los 4 últimos caracteres del número de serie (XXXX) para confirmar". Sin distinguir mayúsculas. Si el disco no informa número de serie (algunos USB), se muestra y se pide el identificador del disco (GUID) con el texto "Escribe los 4 últimos caracteres del identificador del disco" | FR-012, R13 |
| Reducción del origen | Solo si aplica: panel separado con el tamaño actual y el reducido de Windows en el disco original, la explicación de que se restaurará al final y su propia casilla de confirmación | FR-013 |
| Botón | **Clonar ahora** (estilo de acción destructiva). Solo se habilita con las confirmaciones válidas | FR-012 |

## Paso 4: Progreso

| Elemento | Contenido | Requisito |
|----------|-----------|-----------|
| Fases | Lista de fases del plan con su estado (pendiente, en curso, hecha) | FR-019 |
| Progreso | Barra, porcentaje, "X GB de Y GB", velocidad y tiempo restante | FR-019, SC-006 |
| Aviso | "Los cambios que hagas desde ahora no se incluirán en el clon" | Edge case |
| Cancelar | Pide confirmación. En una fase no cancelable muestra "Terminando paso seguro…" | US3 |
| Cierre de ventana | Se trata igual que Cancelar | US3 |

## Paso 5: Resultado

| Resultado | Contenido | Requisito |
|-----------|-----------|-----------|
| Éxito | Resumen de la verificación, duración, estado de WinRE y **pasos siguientes** (apagar, cambiar el disco o elegirlo en el menú de arranque del firmware) | FR-022, FR-023 |
| Cancelado o fallido | Explicación en lenguaje claro, aviso de que "el disco de destino quedó sin inicializar y no se puede usar", estado del disco original y acción sugerida | FR-024 |
| Restauración del origen | Si hubo reducción: confirmación de que se restauró o un aviso destacado con instrucciones | FR-021a |
| Siempre | Botón **Exportar registro** | FR-026 |
