<!--
Sync Impact Report
- Cambio de versión: plantilla (sin versión) → 1.0.0
- Principios (todos nuevos):
  I. Seguridad de los datos primero (NO NEGOCIABLE)
  II. Planificar antes de actuar
  III. Dominio aislado (DDD + arquitectura hexagonal)
  IV. Pruebas primero, con discos virtuales
  V. Solo APIs públicas de Windows (sala limpia)
  VI. Alcance mínimo
  VII. Transparencia y observabilidad
  VIII. Experiencia guiada y localizada
- Secciones añadidas: Restricciones técnicas; Flujo de desarrollo y calidad
- Secciones eliminadas: ninguna
- Plantillas: no requieren cambios (leen la constitución al ejecutarse)
- TODO pendientes:
  TODO(CODE_SIGNING): decidir el certificado de firma de código antes de la primera versión pública
-->

# WinCopy Constitution

## Core Principles

### I. Seguridad de los datos primero (NO NEGOCIABLE)

Clonar implica borrar un disco entero. Cualquier error puede destruir datos de forma irreversible.

- El disco que contiene el Windows en ejecución, o cualquier volumen del sistema, MUST NOT poder elegirse
  como destino. Esta regla se aplica en el dominio, no solo en la interfaz.
- Antes de escribir en el destino, el usuario MUST confirmar de forma explícita e informada qué disco se
  borrará, identificado por modelo, número de serie, tamaño y particiones actuales.
- El disco de origen se trata como de solo lectura. La única excepción permitida es encoger el volumen
  de Windows con la función nativa del sistema (`FSCTL_SHRINK_VOLUME`), que MUST pedir su propia
  confirmación, aparte de la de borrar el destino.
- Si una comprobación previa no se puede verificar, la operación MUST abortarse, no continuar. Las
  comprobaciones mínimas son: capacidad, UEFI/GPT, BitLocker, estado de VSS y que los discos sigan
  siendo los mismos.
- Si ocurre un fallo o el usuario cancela a mitad del proceso, MUST quedar intacto el origen. El destino
  se marca como incompleto y la app no intenta arrancar desde él.
- Al terminar, el resultado MUST verificarse: tabla de particiones, sistemas de archivos que se montan y
  configuración de arranque presente.

Motivo: es la diferencia entre una herramienta útil y una que destruye el equipo del usuario.

### II. Planificar antes de actuar

- Toda operación de clonado se modela como un **plan inmutable** (`ClonePlan`). El dominio lo calcula a
  partir del inventario de discos, y en ese cálculo no se toca ningún disco.
- El plan MUST mostrarse al usuario antes de ejecutarse: particiones, tamaños de origen y destino, si se
  encogerá o ampliará el volumen y los avisos (por ejemplo, BitLocker).
- La ejecución MUST seguir exactamente el plan aprobado. Si el estado real de los discos cambió desde
  que se calculó, la ejecución se aborta y se vuelve a planificar.

Motivo: separar la decisión de la acción hace que el comportamiento sea predecible, revisable y fácil
de probar.

### III. Dominio aislado (DDD + arquitectura hexagonal)

- La capa de dominio MUST NOT depender de Windows, P/Invoke, el sistema de archivos, WPF ni ninguna
  infraestructura. Solo contiene modelos, reglas e interfaces (puertos).
- Los contextos delimitados son:
  - **Inventario de discos**: descubrir discos y particiones.
  - **Planificación del clonado**: el agregado `ClonePlan` y sus reglas.
  - **Ejecución del clonado**: instantánea, copia de bloques y progreso.
  - **Reparación del arranque**: EFI, BCD y entorno de recuperación.
- El acceso a Windows (VSS, IOCTL, FSCTL, `bcdboot`, `reagentc`) se hace solo a través de adaptadores que
  implementan los puertos del dominio.
- Los conceptos se nombran con un lenguaje ubicuo, mantenido en un glosario en `docs/`. El código usa
  los mismos términos que las especificaciones.
- Las reglas de negocio viven en agregados y objetos de valor, no en la interfaz ni en los adaptadores.

Motivo: un dominio puro se puede probar sin tocar discos reales, que es indispensable para el
principio I.

### IV. Pruebas primero, con discos virtuales

- Para el dominio y la lógica de aplicación, las pruebas MUST escribirse antes que la implementación
  (rojo → verde → refactorizar).
- Las pruebas de integración de los adaptadores de disco MUST ejecutarse exclusivamente sobre discos
  virtuales VHDX creados y destruidos por la propia prueba.
- Las pruebas automáticas MUST NOT acceder nunca a discos físicos. Un mecanismo de protección MUST
  impedirlo aunque la prueba esté mal configurada.
- Cada versión MUST validarse de forma manual en un disco físico real (incluido arrancar desde el clon),
  siguiendo una lista de comprobación documentada y registrando el resultado.

Motivo: es la única manera de iterar rápido sin arriesgar los datos del equipo de desarrollo.

### V. Solo APIs públicas de Windows (sala limpia)

- La implementación MUST basarse únicamente en APIs y herramientas documentadas por Microsoft.
- Queda prohibido hacer ingeniería inversa de software de terceros (Macrium Reflect, 4DDiG u otros) y
  usar su código, binarios, iconos, imágenes o textos.
- El código GPL de otros proyectos (por ejemplo, partclone o ntfs-3g) MUST NOT copiarse ni adaptarse.
  Solo se puede consultar documentación conceptual.
- MUST NOT usarse drivers de kernel propios.
- La interfaz puede inspirarse en la estética de 4DDiG, pero con marca y recursos propios.

Motivo: el proyecto es público y de portafolio. Necesita una procedencia limpia y defendible.

### VI. Alcance mínimo

- La única funcionalidad del producto es clonar el disco del sistema Windows a otro disco.
- La versión 1 solo admite sistemas UEFI con disco GPT en origen y destino.
- Cualquier funcionalidad adicional (BIOS/MBR, copias de seguridad, gestión de particiones, otros
  discos) MUST aprobarse antes con una enmienda de esta constitución.
- Se prefiere la solución más simple que cumpla la especificación (YAGNI).

Motivo: un alcance acotado reduce la superficie de errores en una operación destructiva.

### VII. Transparencia y observabilidad

- Cada operación sobre un disco MUST registrarse en un log estructurado: qué se hizo, en qué disco y
  desplazamiento, cuánto tiempo tardó y el resultado.
- Los errores mostrados al usuario MUST explicar qué pasó y qué puede hacer, sin códigos crudos sin
  explicación. El detalle técnico va al log.
- El progreso MUST ser real: bytes copiados respecto al total y tiempo restante estimado.
- La aplicación MUST NOT enviar datos a ningún servidor (sin telemetría). El usuario puede exportar los
  logs para pedir ayuda.

Motivo: una operación larga y destructiva solo genera confianza si el usuario ve qué está pasando.

### VIII. Experiencia guiada y localizada

- La interfaz es un asistente por pasos: seleccionar el destino, revisar el plan, confirmar, ver el
  progreso y ver el resultado.
- Todos los textos visibles MUST estar en archivos de recursos (`.resx`) en español e inglés. No se
  admiten textos escritos directamente en el código.
- El idioma se elige según el idioma de Windows, con inglés como respaldo.
- La interfaz MUST poder usarse solo con el teclado y con un lector de pantalla en los pasos críticos
  (elegir el destino y confirmar).

Motivo: la claridad de la interfaz es parte de la seguridad (principio I).

## Restricciones técnicas

- **Lenguaje y plataforma:** C# con .NET 10 y WPF, siguiendo el patrón MVVM. Solo x64.
- **Sistemas operativos:** Windows 10 22H2 y Windows 11, x64, arrancados en modo UEFI.
- **Permisos:** la aplicación se ejecuta como administrador (`requireAdministrator` en el manifiesto).
- **Acceso al sistema:** P/Invoke a Win32 y COM para VSS. Cualquier dependencia de terceros MUST tener una
  licencia compatible con un proyecto público (MIT, Apache-2.0, BSD o MS-PL) y justificarse en el plan.
- **Instalador:** MSI generado con WiX y definido en el repositorio.
- **Firma de código:** TODO(CODE_SIGNING). Hay que decidirla antes de la primera versión pública. Smart
  App Control y otras políticas de integridad de Windows pueden bloquear binarios sin firmar.
- **Proyectos:** se configuran con `Nullable` activado, `TreatWarningsAsErrors` y los analizadores de .NET.

## Flujo de desarrollo y calidad

- Toda funcionalidad sigue el flujo de Spec Kit: `specify` → `clarify` → `plan` → `tasks` →
  `implement`. Cada plan MUST incluir una sección "Constitution Check" que revise estos principios.
- Cada funcionalidad se desarrolla en su propia rama. Los commits siguen Conventional Commits.
- Para integrar una rama en `main` hace falta:
  1. Que compile sin advertencias.
  2. Que todas las pruebas pasen.
  3. Que se revise el cumplimiento de la constitución.
  4. Que las especificaciones estén actualizadas si el comportamiento cambió.
- Las decisiones de arquitectura relevantes se documentan en `docs/` como registros de decisión.

## Governance

- Esta constitución prevalece sobre cualquier otra práctica o documento del proyecto.
- Las enmiendas se hacen con `/speckit-constitution`. Cada enmienda MUST documentar el cambio, su motivo
  y su impacto en las especificaciones existentes.
- Versionado semántico:
  - MAJOR: se elimina o redefine un principio.
  - MINOR: se añade un principio o sección, o se amplía de forma significativa.
  - PATCH: aclaraciones de redacción.
- Las desviaciones necesarias de un principio MUST justificarse en la sección "Complexity Tracking" del
  plan correspondiente. El principio I nunca admite desviaciones.
- Las decisiones iniciales del proyecto están en `docs/decisiones-iniciales.md`.

**Version**: 1.0.0 | **Ratified**: 2026-10-13 | **Last Amended**: 2026-10-13
