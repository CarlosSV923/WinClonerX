# Pendientes para la versión 1.0

Estado a 2026-10-22 (rama `001-system-disk-clone`, commit 05ae16c). Esta lista reúne lo que la constitución (v1.1.1) y [tasks.md](../specs/001-system-disk-clone/tasks.md) exigen antes de la versión 1.0, con el estado actual de cada punto.

Un punto se marca como hecho solo cuando tasks.md lo marca como hecho y se ha comprobado. Los puntos sin marcar están abiertos.

## 1. Pruebas de integración sobre discos virtuales

**Motivo:** las pruebas que usan VHDX y VSS provocaron pantallas azules (BSOD) en el equipo de desarrollo. Por eso, las pruebas de disco administrativas se ejecutan solo en una máquina virtual de Hyper-V.

**Decisión del autor (2026-10-22):** se aplazan mientras la app sea de uso personal. La validación se hace en hardware real (punto 4). Las pruebas siguen en el repositorio: si la app pasa a ser pública o se cambia el código de copia, tabla de particiones o arranque, conviene ejecutarlas (en una máquina virtual o en un equipo de pruebas, nunca en el equipo de desarrollo).

- [ ] **T042 a T045:** inventario, instantánea VSS, clonado completo a un disco mayor y verificación estructural. **Estado:** escritas, pero no ejecutadas. Falta ejecutarlas en una máquina virtual.
- [ ] **T087:** clonado a un disco más pequeño (origen de 40 GB con 8 GB de datos, destino de 25 GB). **Estado:** no escrita. La reducción temporal ya se validó en hardware real varias veces (2026-10-20 a 2026-10-22).
- [ ] **T098:** cancelación al 50 % de la copia, que debe dejar el destino sin tabla de particiones y el origen sin cambios. **Estado:** no escrita. La cancelación ya se validó en hardware real (punto 4).
- [ ] **T111:** proporción de la duración respecto a los datos usados (SC-003). **Estado:** no escrita.
- [ ] **T112:** rendimiento (≥ 85 % de la velocidad secuencial), memoria (< 300 MB) y respuesta de la interfaz. **Estado:** no escrita.

## 2. Verificación completa de datos (FR-022a, SC-009)

- [x] **T107 y T108:** fase `FullVerification` y casilla "Verificar todos los datos". **Estado:** hechas y validadas en hardware real el 2026-10-22 (commit 05ae16c): 124 GB copiados por USB y verificados sin diferencias. La verificación compara el destino con huellas SHA-256 tomadas durante la copia (research R18), porque el origen no es estable: VSS no congela `pagefile.sys`, `hiberfil.sys` ni `swapfile.sys`, y la partición EFI se copia en vivo.
- [ ] **T106:** pruebas de la verificación completa con una diferencia inyectada. **Estado:** las pruebas unitarias están hechas; la de integración (`FullVerificationTests`) está escrita, pero no ejecutada (necesita una máquina virtual).

## 3. Cancelación, desconexión y registro (historia 3)

La historia 3 está implementada y validada en hardware real: cancelación con confirmación, exportación del registro (T097, T103–T105) y desconexión de discos con clasificación de errores de E/S (T096, T101, T102). Además, "Volver a empezar" tras un fallo o una cancelación.

## 4. Validación en hardware real

- [x] **Cancelación en hardware real.** **Estado:** validada el 2026-10-21 (3c05c93): copia detenida al 7 %, instantánea borrada, C: restaurada y resultado `Cancelled`.
- [x] **Desconexión de un disco durante el clonado en hardware real.** **Estado:** validada el 2026-10-22 (9bc99e7): WD desconectado durante la copia, resultado `Failed` con `TargetDeviceRemoved`, C: restaurada.
- [x] **Diario de restauración del origen en hardware real**, con un cierre forzado y C: reducido. **Estado:** validado el 2026-10-22 (9bc99e7): al reabrir, `PendingSourceRestoreFound` y C: restaurada con el aviso verde.
- [ ] **Cierre inesperado de la aplicación en PrepareTarget** (mini PC, commit 8467029). **Estado:** sin explicación. No se ha vuelto a reproducir, y no hay registro ni datos del visor de eventos. Si vuelve a pasar, guarda el archivo `.clef` de `C:\ProgramData\WinClonerX\logs` y el visor de eventos, y comprueba que el diario restaura C:.
- [x] **Nivel 4 completo en el segundo disco físico, con desinstalación** (T119). **Estado:** superado el 2026-10-23 con el MSI 0.9.0 en el mini PC: clonado con verificación, arranque del clon, SC-001 (42 s), SC-006 (−9 %) y desinstalación limpia (ver [validación manual](validacion-manual.md)).
- [ ] **Niveles 1 a 3 de la guía de validación** (T118). **Estado:** pendiente. El punto de control S2 se hizo en hardware real, no en la máquina virtual del nivel 3. El nivel 3 es opcional, pero conviene ejecutarlo.
- [ ] **Corregir la referencia de T073.** La tarea menciona `docs/spikes/s2-arranque-vm.md`, que no existe. El resultado está en [s2-arranque-hardware-real.md](spikes/s2-arranque-hardware-real.md). **Estado:** pendiente.
- [x] **Codificación de la salida de `bcdedit` y `reagentc`** (acentos mal leídos). **Estado:** corregida en 04fe7aa (página de códigos OEM).

## 5. Accesibilidad, textos y uso

- [x] **Revisión manual de accesibilidad** con Accessibility Insights y Narrator. **Estado:** descartada por decisión del autor (2026-10-22): la app es de uso personal. Se comprobaron en hardware real el teclado y los colores. Si la app pasa a ser pública, hay que hacerla.
- [x] **T120:** prueba de uso con al menos 5 personas sin perfil técnico (SC-007). **Estado:** descartada por decisión del autor (2026-10-22): la app es de uso personal. Si la app pasa a ser pública, hay que hacerla.

## 6. Distribución y firma

- [ ] **Evaluar la firma gratuita para código abierto** (SignPath Foundation): requisitos, solicitud y decisión. La constitución exige hacerlo antes de la versión 1.0. **Estado:** pendiente.
- [x] **Probar el flujo de publicación** (`.github/workflows/release.yml`, T113). **Estado:** probado el 2026-10-22 con la etiqueta `v0.9.0`: pre-release con `WinClonerX-0.9.0.msi` (54 MB), el SHA-256 de las notas coincide con el archivo descargado y `gh attestation verify` es correcto.
- [ ] **Revisar la sección de instalación del README** después de la primera versión publicada, con las pantallas reales de SmartScreen y las instrucciones de Smart App Control. **Estado:** pendiente.

## 7. Licencia y dependencias

- [x] **Licencia del proyecto.** **Estado:** MIT, añadida el 2026-10-22 (archivo LICENSE).
- [ ] **Revisar las licencias de las dependencias.** Según el plan, las dependencias tienen licencias compatibles con un proyecto público (MIT, Apache-2.0, BSD o MS-RL). El instalador usa WiX (MS-RL). Hay que volver a revisar la tarifa de mantenimiento de WiX si el proyecto llega a tener ingresos (research R16). **Estado:** pendiente de confirmar al cerrar la versión.
