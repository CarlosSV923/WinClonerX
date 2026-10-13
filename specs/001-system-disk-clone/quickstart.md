# Quickstart: Validación del clonado del disco del sistema

Guía para comprobar que la funcionalidad cumple la [especificación](spec.md). Va de lo más seguro
(pruebas automáticas sin discos) a lo más real (disco físico).

## Requisitos previos

- Windows 11 o Windows 10 22H2 x64, arrancado en UEFI.
- .NET SDK 10.0.x (`dotnet --list-sdks`).
- Una terminal **como administrador** para las pruebas de integración y la app.
- Opcional, para el nivel 3: Hyper-V habilitado (Windows Pro) y una ISO de Windows 11.
- Nivel 4: el segundo disco físico (se **borrará**).

## Nivel 1: Pruebas unitarias (sin privilegios ni discos)

```powershell
dotnet test tests/WinCopy.Domain.Tests
dotnet test tests/WinCopy.Application.Tests
dotnet test tests/WinCopy.App.Tests
dotnet test tests/WinCopy.Infrastructure.Windows.Tests
```

Resultado esperado: todo en verde. Cubren:
- Invariantes del `ClonePlan` y cálculo del diseño ([data-model.md](data-model.md)).
- Máquina de estados de `CloneExecution` con fallos inyectados en cada fase. El origen nunca se modifica
  fuera de ShrinkSource y RestoreSource, y en caso de fallo siempre se ejecutan las compensaciones (SC-005).
- Serialización GPT y CRC32, y paridad de claves `.resx` en español e inglés (SC-008).

## Nivel 2: Integración con discos virtuales (administrador)

```powershell
dotnet test tests/WinCopy.Infrastructure.IntegrationTests
```

- Los fixtures crean VHDX temporales en `%TEMP%\WinCopyTests\`, los adjuntan, los usan y los eliminan.
- **Comprobación de seguridad**: el fixture rechaza cualquier disco que no sea `FileBackedVirtual` creado
  por él mismo (principio IV). Hay un test específico que intenta usar un disco físico y debe fallar
  antes de abrirlo.
- Escenarios mínimos:
  - Clonar un VHDX GPT con ESP, MSR, NTFS y Recovery a un VHDX más grande, y a uno más pequeño con
    reducción del origen y su restauración.
  - Cancelar en mitad de CopyData: el destino queda sin tabla de particiones y el origen intacto.
  - Verificación completa: detecta un byte alterado a propósito (SC-009).
  - Spikes S1 y S3 ([research.md](research.md)): escritura en un disco fuera de línea y BitLocker sobre
    VHDX.

## Nivel 3: Arranque en máquina virtual (opcional, recomendado)

1. Crear una VM Gen2 de Hyper-V con Windows 11 (disco de 64 GB) y añadirle un segundo VHDX vacío de
   80 GB (destino mayor) o de 40 GB (destino menor).
2. Instalar el MSI de WinCopy dentro de la VM y clonar al segundo disco.
3. Apagar la VM, quitar el disco original y arrancar desde el clon.
4. Comprobar:
   - Windows arranca (SC-002) con la unidad `C:`, los mismos usuarios, programas y archivos.
   - `reagentc /info` muestra WinRE habilitado (spike S2).
   - En el caso de destino menor: volver a arrancar desde el original y comprobar que `C:` tiene su
     tamaño original (SC-010).
5. Con los dos discos conectados, comprobar que ambos aparecen en línea en el Administrador de discos
   (FR-017).

## Nivel 4: Disco físico (validación manual por versión)

> ⚠️ Se borrará todo el contenido del segundo disco.

1. Instalar el MSI y abrir WinCopy.
2. Comprobar que el disco del sistema no aparece como destino (FR-004) y que el segundo disco muestra el
   modelo y el número de serie correctos.
3. Revisar el plan y confirmar escribiendo los 4 últimos caracteres del número de serie. Cronometrar
   desde abrir la app hasta pulsar "Clonar ahora": debe ser menos de 3 minutos (SC-001).
4. Durante la copia, anotar el tiempo restante estimado al 10 % y compararlo con la duración real
   (±20 %, SC-006).
5. Al terminar, exportar el registro.
6. Apagar, arrancar desde el disco clonado (menú de arranque del firmware o intercambiando los discos) y
   repetir las comprobaciones del nivel 3.
7. Registrar el resultado en `docs/validacion-manual.md`: versión, hardware, tamaños, duración y
   resultado de cada comprobación.

## Criterios de aceptación de la funcionalidad

| Criterio | Dónde se valida |
|----------|-----------------|
| SC-001 (< 3 min, ≤ 5 decisiones) | Nivel 4, paso 3 |
| SC-002 (arranque 100 %) | Niveles 3 y 4 |
| SC-003 (duración proporcional a lo ocupado) | Nivel 2 (VHDX de 200 GB y de 1 TB con los mismos datos) |
| SC-004 (nunca se escribe en un disco no confirmado) | Niveles 1 y 2 (tests de invariantes y fixture de seguridad) |
| SC-005 (el origen sobrevive a fallos) | Niveles 1 y 2 |
| SC-006 (tiempo restante ±20 %) | Nivel 4, paso 4 |
| SC-007 (identificar el disco ≥ 90 %) | Prueba de uso con 5 o más personas antes de la 1.0 |
| SC-008 (es/en 100 %) | Nivel 1 |
| SC-009 (detección de alteraciones) | Nivel 2 |
| SC-010 (restauración del origen) | Niveles 2 y 3 |
