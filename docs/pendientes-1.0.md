# Pendientes para la versión 1.0

Estado a 2026-10-20 (rama `001-system-disk-clone`, commit 322fedf). Esta lista reúne lo que la constitución (v1.1.1) y [tasks.md](../specs/001-system-disk-clone/tasks.md) exigen antes de la versión 1.0, con el estado actual de cada punto.

Un punto se marca como hecho solo cuando tasks.md lo marca como hecho y se ha comprobado. Los puntos sin marcar están abiertos.

## 1. Pruebas de integración sobre discos virtuales

**Motivo:** las pruebas que usan VHDX y VSS provocaron pantallas azules (BSOD) en el equipo de desarrollo. Por eso, las pruebas de disco administrativas se ejecutan solo en una máquina virtual de Hyper-V.

- [ ] **T042 a T045:** inventario, instantánea VSS, clonado completo a un disco mayor y verificación estructural. **Estado:** escritas, pero no ejecutadas. Falta ejecutarlas en una máquina virtual.
- [ ] **T087:** clonado a un disco más pequeño (origen de 40 GB con 8 GB de datos, destino de 25 GB). **Estado:** no escrita.
- [ ] **T098:** cancelación al 50 % de la copia, que debe dejar el destino sin tabla de particiones y el origen sin cambios. **Estado:** no escrita.
- [ ] **T111:** proporción de la duración respecto a los datos usados (SC-003). **Estado:** no escrita.
- [ ] **T112:** rendimiento (≥ 85 % de la velocidad secuencial), memoria (< 300 MB) y respuesta de la interfaz. **Estado:** no escrita.

## 2. Verificación completa de datos (FR-022a, SC-009)

- [ ] **T106:** pruebas de la verificación completa, con una diferencia inyectada. **Estado:** no escritas.
- [ ] **T107:** fase `FullVerification` que compara el contenido copiado con el del origen. **Estado:** abierta. El dominio ya define la fase, pero la tarea no está marcada como hecha.
- [ ] **T108:** casilla "Verificar todos los datos (aprox. duplica la duración)" en la revisión. **Estado:** abierta. La casilla no está conectada y se queda desactivada.

## 3. Cancelación, desconexión y registro (historia 3)

Ya existen la cancelación con confirmación en la pantalla de progreso y la exportación del registro (T097, T103, T104, T105, commit f5865af). La prueba en hardware real de la cancelación está en el punto 4.

- [ ] **T096:** pruebas de la desconexión de disco y del error de lectura del origen. **Estado:** no escritas con el nombre previsto. Hay pruebas de `CancelExecution`, pero no cubren la desconexión.
- [ ] **T101:** `DeviceChangeMonitor`, que aborta el clonado si se desconecta un disco. **Estado:** abierta. Falta el monitor dedicado para la desconexión de un disco durante el clonado.
- [ ] **T102:** clasificación de errores de E/S (origen, destino, dispositivo no disponible). **Estado:** abierta.

## 4. Validación en hardware real

- [ ] **Cancelación en hardware real** (commit f5fe7ca). **Estado:** pendiente.
- [ ] **Diario de restauración del origen en hardware real** (commit 37757d8), incluido un cierre inesperado con C: reducido. **Estado:** pendiente. La validación de 2026-10-20 se hizo con 8467029, que no incluye el diario.
- [ ] **Cierre inesperado de la aplicación en PrepareTarget** (mini PC, commit 8467029). **Estado:** sin explicación. No se ha vuelto a reproducir, y no hay registro ni datos del visor de eventos. Si vuelve a pasar, guarda el archivo `.clef` de `C:\ProgramData\WinCopy\logs` y el visor de eventos, y comprueba que el diario restaura C:.
- [ ] **Nivel 4 completo en el segundo disco físico, con desinstalación** (T119). **Estado:** pendiente. El punto de control S2 se superó el 2026-10-20 (ver [validación manual](validacion-manual.md)), pero falta la lista completa, incluida la desinstalación.
- [ ] **Niveles 1 a 3 de la guía de validación** (T118). **Estado:** pendiente. El punto de control S2 se hizo en hardware real, no en la máquina virtual del nivel 3. El nivel 3 es opcional, pero conviene ejecutarlo.
- [ ] **Corregir la referencia de T073.** La tarea menciona `docs/spikes/s2-arranque-vm.md`, que no existe. El resultado está en [s2-arranque-hardware-real.md](spikes/s2-arranque-hardware-real.md). **Estado:** pendiente.
- [ ] **Codificación de la salida de `bcdedit` y `reagentc`** (acentos mal leídos). **Estado:** abierta. No afecta al análisis, pero conviene corregirla.

## 5. Accesibilidad, textos y uso

- [ ] **T109:** revisión de accesibilidad de los pasos 1 y 3 con Accessibility Insights, con navegación por teclado y lector de pantalla (FR-030, principio VIII). **Estado:** pendiente.
- [ ] **T110:** revisión editorial de los textos en español e inglés. Los errores deben decir qué pasó y qué puede hacer la persona (principio VII). **Estado:** pendiente.
- [ ] **T120:** prueba de uso con al menos 5 personas sin perfil técnico. Cada una debe identificar el disco que se borrará antes de confirmar (SC-007, al menos 90 % de aciertos). **Estado:** pendiente. Hace falta el protocolo y los resultados en [validación manual](validacion-manual.md).

## 6. Distribución y firma

- [ ] **Evaluar la firma gratuita para código abierto** (SignPath Foundation): requisitos, solicitud y decisión. La constitución exige hacerlo antes de la versión 1.0. **Estado:** pendiente.
- [ ] **Probar el flujo de publicación** (`.github/workflows/release.yml`, T113) con una etiqueta de prueba. Hay que comprobar el MSI, el SHA-256, la atestación con `gh attestation verify` y las notas de la versión. **Estado:** el flujo está escrito, pero no se ha probado.
- [ ] **Revisar la sección de instalación del README** después de la primera versión publicada, con las pantallas reales de SmartScreen y las instrucciones de Smart App Control. **Estado:** pendiente.

## 7. Licencia y dependencias

- [ ] **Licencia del proyecto.** El repositorio no tiene archivo LICENSE. Hay que decidir la licencia y añadir el archivo. **Estado:** pendiente. El README dice que la licencia está pendiente.
- [ ] **Revisar las licencias de las dependencias.** Según el plan, las dependencias tienen licencias compatibles con un proyecto público (MIT, Apache-2.0, BSD o MS-RL). El instalador usa WiX (MS-RL). Hay que volver a revisar la tarifa de mantenimiento de WiX si el proyecto llega a tener ingresos (research R16). **Estado:** pendiente de confirmar al cerrar la versión.
