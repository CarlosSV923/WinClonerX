# WinCopy — Decisiones iniciales

Fecha: 2026-10-13
Estado: aprobadas

## Alcance

Aplicación de Windows instalable cuya **única funcionalidad** es clonar el disco del sistema (por ejemplo, de un HDD a un SSD). Cualquier otra función de gestión de particiones o copias de seguridad queda fuera del alcance por ahora.

## Contexto

- Se analizó usar REA (github.com/morluto/rea) para hacer ingeniería inversa de Macrium Reflect y 4DDiG Partition Manager.
- Conclusión: **no es necesario**. Todo el proceso de clonado se puede hacer con APIs públicas y documentadas de Windows:
  - VSS para la instantánea en caliente.
  - `FSCTL_GET_VOLUME_BITMAP` para leer solo los bloques ocupados.
  - `IOCTL_DISK_CREATE_DISK` y `IOCTL_DISK_SET_DRIVE_LAYOUT_EX` para la tabla de particiones.
  - `FSCTL_SHRINK_VOLUME` y `FSCTL_EXTEND_VOLUME` para ajustar tamaños.
  - **Actualización 2026-10-13**: para reducir se usa `MSFT_Partition.Resize` (Storage Management API).
    `FSCTL_SHRINK_VOLUME` sola obliga a mover los archivos por cuenta propia. Ver
    `specs/001-system-disk-clone/research.md` (R6) y la constitución v1.1.1.
  - `bcdboot` y `reagentc` para el arranque y la recuperación.
- Equipo de desarrollo: UEFI con disco GPT, NVMe Samsung 990 EVO Plus de 1 TB (C: de 930 GB), .NET SDK 10.0.401 y Node 24.

## Decisiones

| # | Tema | Decisión | Motivo |
|---|------|----------|--------|
| 1 | Destino más pequeño que el origen | Se admite si el destino tiene espacio suficiente para los datos usados. Antes de clonar, la app **encoge C: con la función nativa de Windows** (`MSFT_Partition.Resize`; ver la nota de actualización). Si el destino es mayor, el volumen se amplía después de clonar. | Cubre el caso típico de HDD→SSD con riesgo moderado y usa la misma función que el Administrador de discos. Se descartó reubicar NTFS por nuestra cuenta, por complejidad y riesgo. La app debe avisar de que se modifica el origen y pedir confirmación. |
| 2 | Firmware y particionado | **Solo UEFI con GPT** | Es el estándar de Windows 11 y el entorno de desarrollo. BIOS/MBR y la conversión MBR→GPT quedan para más adelante. |
| 3 | Tecnología | **C# con .NET 10 y WPF**, con llamadas directas (P/Invoke) a las APIs de Windows | Misma tecnología de interfaz que 4DDiG (facilita una estética similar), es madura y el SDK ya está instalado. |
| 4 | Instalador | **MSI con WiX** | Estándar profesional, se define en código (encaja con SDD) y permite una desinstalación limpia. La app pide permisos de administrador (`requireAdministrator`). |
| 5 | Desarrollo guiado por especificaciones | **GitHub Spec Kit** | Flujo constitution → specify → clarify → plan → tasks → implement, con trazabilidad. Se combina con una arquitectura DDD y hexagonal. |
| 6 | Legal y visual | **Sin ingeniería inversa; estilo propio** | Solo APIs públicas. Interfaz inspirada en la estética de 4DDiG (distribución, asistente por pasos, paleta), con marca, iconos y recursos propios. El repositorio puede ser público. |
| 7 | BitLocker | **Detectar y avisar** | Si el origen está cifrado, se advierte que el clon quedará sin cifrar y se explica cómo reactivar BitLocker. |
| 8 | Idioma | **Español e inglés** | Textos en recursos `.resx` desde el inicio; el idioma se elige según el de Windows. |
| 9 | Pruebas | **Discos VHDX y un segundo disco físico** | VHDX para desarrollo y pruebas automáticas. El segundo disco físico disponible se usa para la validación real, incluido arrancar desde el clon. |

## Reglas de seguridad iniciales (para la especificación)

- El disco del sistema nunca puede elegirse como destino.
- Antes de escribir en el destino, el usuario debe confirmarlo de forma explícita e informada (se mostrará qué disco se borrará).
- Las comprobaciones previas deben pasar antes de tocar cualquier disco: capacidad, UEFI/GPT, BitLocker y estado de VSS.
