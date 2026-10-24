# WinClonerX

<p align="center"><img src="assets/icon/winclonerx-256.png" width="96" alt="Icono de WinClonerX"></p>

Antes se llamaba WinCopy.

> **In short (English):** WinClonerX copies the Windows system disk to another disk, for example from an HDD to an SSD, so you can boot Windows from the new disk. It only does that. It works only on UEFI systems with a GPT disk, on Windows 10 22H2 and Windows 11 (x64). The project is in development. The installer is not signed, so Windows SmartScreen will show a warning. The installation section explains how to continue.

## Qué es

WinClonerX copia el disco donde está instalado Windows a otro disco. Después, el disco nuevo puede arrancar Windows con los mismos usuarios, programas y archivos. El caso típico es pasar de un disco duro (HDD) a un SSD.

La interfaz tiene cinco pasos y está en español e inglés. El idioma se elige según el idioma de Windows.

## Qué no es

- No es un programa de copias de seguridad. No crea archivos de copia ni imágenes. Copia un disco a otro disco.
- No es un gestor de particiones. Solo prepara el disco de destino para el clon y, si hace falta, reduce temporalmente la partición de Windows del origen.
- No convierte discos ni sistemas. No funciona con BIOS/MBR ni con discos MBR.
- No clona otros discos que no sean el del sistema. Tampoco admite discos dinámicos, RAID ni Espacios de almacenamiento.
- No envía datos a internet. No tiene telemetría.

## Requisitos

- Windows 10 versión 22H2 o Windows 11, de 64 bits (x64).
- El equipo arranca en modo UEFI y el disco del sistema usa GPT. WinClonerX comprueba esto antes de clonar. Si no se cumple, no clona.
- Una sola instalación de Windows en el disco de origen. No puede haber arranque dual.
- Cuenta de administrador. WinClonerX pide permiso de administrador al abrirse.
- Un disco de destino conectado, interno o por USB, sin volúmenes en uso.
- El destino debe tener el mismo tamaño de sector lógico que el origen (512 o 4096 bytes).
- El destino debe tener espacio para los datos usados más un margen. El margen es el 10 % de los datos usados, con un mínimo de 10 GiB.
- En un portátil, conecta el cargador antes de empezar.

**Atención:** el disco de destino se borra por completo. Antes de empezar, guarda lo que necesites de ese disco.

## Qué pasa con BitLocker

Si la partición de Windows está cifrada con BitLocker, WinClonerX avisa. Para continuar, debes aceptar que el clon quedará sin cifrar. Después de clonar, activa BitLocker en el disco nuevo desde Windows.

## Cambio temporal del tamaño de C

Si el disco de destino es más pequeño que el origen, WinClonerX reduce temporalmente la partición de Windows (C:) del origen. Usa la misma función de Windows que Administración de discos. WinClonerX pide una confirmación aparte para esta reducción.

Al terminar, WinClonerX vuelve a ampliar C: a su tamaño original. Lo hace tanto si el clonado sale bien como si se cancela o falla.

Si WinClonerX se cierra o el equipo se apaga durante el clonado, WinClonerX vuelve a ampliar C: al abrirse de nuevo. Para eso guarda un diario en `C:\ProgramData\WinClonerX\pending-source-restore.json`. Esta restauración automática después de un cierre inesperado todavía no se ha probado en hardware real.

Si WinClonerX no consigue ampliar C:, lo indica en la pantalla de resultado. El espacio que quede sin asignar se puede recuperar en Administración de discos, con clic derecho en C: y la opción Extender volumen.

## Instalación

El instalador no está firmado. El proyecto no paga un certificado de firma. Por eso, Windows muestra avisos al instalarlo. Sigue los pasos en este orden.

Descarga el archivo `WinClonerX-X.Y.Z.msi` de la página de versiones del repositorio: https://github.com/CarlosSV923/WinClonerX/releases

### 1. Superar el aviso de SmartScreen

Al ejecutar el instalador, Windows SmartScreen puede mostrar el aviso "Windows protegió su PC". Pulsa **Más información** y después **Ejecutar de todas formas**.

### 2. Verificar la huella SHA-256 y la atestación

Cada versión publica la huella SHA-256 del instalador en sus notas. Compara la huella de tu archivo con la de las notas:

```powershell
Get-FileHash .\WinClonerX-X.Y.Z.msi -Algorithm SHA256
```

Cada versión también tiene una atestación de procedencia de GitHub. Comprueba que el instalador se compiló en este repositorio. Para esto necesitas la CLI de GitHub (`gh`):

```powershell
gh attestation verify .\WinClonerX-X.Y.Z.msi --repo CarlosSV923/WinClonerX
```

Si la huella no coincide o la atestación no se verifica, no instales el archivo.

### 3. Compilar desde el código

Si prefieres no instalar un archivo ya compilado, puedes compilarlo tú mismo. Necesitas Windows 10 o 11 de 64 bits y el SDK de .NET 10. Consulta la sección [Compilar desde el código](#compilar-desde-el-código).

### 4. Solo como último recurso: desactivar Smart App Control

> **Advertencia.** Smart App Control es una protección de Windows 11. En muchas versiones de Windows, no se puede volver a activar sin reinstalar Windows o restablecer el sistema. Desactívalo solo si los pasos anteriores no funcionan y entiendes el riesgo.

Para desactivarlo, abre **Seguridad de Windows**, entra en **Control de aplicaciones y navegador** y después en la configuración de Smart App Control. Elige **Desactivado**. Los nombres de los menús pueden cambiar según la versión de Windows.

WinClonerX no desactiva Smart App Control ni ninguna otra protección de Windows.

## Cómo se usa

1. **Discos.** WinClonerX detecta el disco del sistema y lo muestra como origen. No se puede cambiar. Elige el disco de destino entre los demás discos. Si un disco no sirve, la pantalla indica el motivo.
2. **Revisión.** Revisa el plan. Muestra qué particiones se copian, qué tamaño tendrán en el destino, si se reduce C: y cuánto tiempo puede tardar. Acepta los avisos, como el de BitLocker, para continuar.
3. **Confirmación.** Escribe los cuatro últimos caracteres del número de serie del disco de destino. Así confirmas que es el disco correcto. Si el plan reduce C:, confirma esa reducción aparte.
4. **Progreso.** Ves la fase actual, el porcentaje, los datos copiados y el tiempo restante. Puedes pulsar **Cancelar**. WinClonerX pide confirmación antes. Algunas fases no se pueden interrumpir. En ese caso, la cancelación espera a que la fase termine.
5. **Resultado.** Muestra si la estructura del disco nuevo es correcta, cuánto tardó y qué pasos seguir. Si el clonado se cancela o falla, WinClonerX explica qué pasó. El disco original sigue funcionando. El disco de destino queda sin inicializar y no sirve para arrancar Windows.

La verificación completa de todos los datos todavía no está disponible en esta versión.

## Después de clonar

1. Apaga el equipo. Para arrancar desde el disco nuevo, puedes dejar los dos discos conectados y elegir el disco nuevo en el menú de arranque del firmware. La tecla para abrir ese menú depende del fabricante. Como alternativa, retira el disco original y arranca.
2. Comprueba que Windows arranca y que C: es la partición de Windows.
3. Abre el símbolo del sistema o PowerShell como administrador y ejecuta:

   ```powershell
   reagentc /info
   ```

   La línea de estado de Windows RE debe indicar que está habilitado (`Enabled`). Si aparece deshabilitado, ejecuta `reagentc /enable`.

WinClonerX no cambia el orden de arranque del firmware.

## Registros

Los registros de cada ejecución están en la carpeta `C:\ProgramData\WinClonerX\logs`. La carpeta `ProgramData` está oculta. Para abrirla, escribe la ruta en la barra de direcciones del Explorador de archivos. Cada ejecución crea un archivo `winClonerX-AAAAMMDD-HHMMSS-<id>.clef`. Es texto con un evento por línea, en formato JSON. Los registros que quedaron en `C:\ProgramData\WinCopy\logs` de versiones antes del cambio de nombre se conservan allí; WinClonerX no los migra ni los borra.

La pantalla de resultado tiene dos botones para los registros:

- **Abrir carpeta de registros** abre esa carpeta en el Explorador.
- **Exportar registro** guarda un `.zip` con el registro de la ejecución. El inventario de discos (modelo, número de serie y tamaño) solo se incluye si marcas la casilla correspondiente, que está desmarcada por defecto.

Si pides ayuda, adjunta ese `.zip`.

## Desinstalar

Ve a **Configuración → Aplicaciones → Aplicaciones instaladas**, busca WinClonerX y elige **Desinstalar**. También puedes usar Programas y características del Panel de control.

La desinstalación quita los archivos del programa y el acceso directo del menú Inicio. No borra los registros de `C:\ProgramData\WinClonerX`. Si no los necesitas, bórralos a mano.

## Compilar desde el código

Necesitas Windows 10 o 11 de 64 bits y el SDK de .NET 10 (10.0.x).

```powershell
git clone https://github.com/CarlosSV923/WinClonerX.git
cd WinClonerX
dotnet restore WinClonerX.slnx
dotnet build WinClonerX.slnx -c Release
```

El MSI solo se genera en la configuración `Release`. Queda en `installer\WinClonerX.Installer\bin\x64\Release\`. La compilación de Release también publica la aplicación como ejecutable independiente para win-x64.

Pruebas sin privilegios ni discos especiales:

```powershell
dotnet test --filter "Category!=Integration"
```

Las pruebas de integración usan discos virtuales VHDX. Necesitan ejecutarse como administrador y crean sus archivos en `%TEMP%\WinClonerXTests`:

```powershell
dotnet test --filter "Category=Integration"
```

Ejecútalas en una máquina virtual, no en tu equipo principal. En el equipo de desarrollo del proyecto, estas pruebas provocaron pantallas azules.

Estructura del repositorio:

- `src/`: código de la aplicación (dominio, aplicación, adaptadores de Windows e interfaz WPF).
- `tests/`: pruebas unitarias y de integración.
- `installer/`: proyecto WiX del MSI.
- `specs/`: especificación, plan y tareas.
- `docs/`: glosario, decisiones, validaciones y pendientes.

## Cómo está hecho

- **Spec Kit.** El desarrollo sigue el flujo de GitHub Spec Kit: especificación, plan, tareas e implementación. La constitución del proyecto está en `.specify/memory/constitution.md`. La especificación está en `specs/001-system-disk-clone/`.
- **DDD y arquitectura hexagonal.** El dominio no depende de Windows. Los casos de uso usan puertos. Los adaptadores de Windows implementan esos puertos. El glosario está en `docs/glosario.md`.
- **Sala limpia.** Solo se usan APIs y herramientas documentadas por Microsoft. Entre ellas, VSS, la Storage Management API, `bcdboot`, `bcdedit` y `reagentc`. No se hace ingeniería inversa de software de terceros. No se usan drivers propios.
- **Seguridad de los datos.** Las pruebas automáticas no abren discos físicos. Hay un único punto en el código por el que se abre un disco para escribir (`DiskWriteGuard`), y ese punto rechaza siempre el disco del sistema.

## Estado del proyecto

WinClonerX está en desarrollo. La versión 1.0 todavía no está lista. Las tareas pendientes están en [docs/pendientes-1.0.md](docs/pendientes-1.0.md).

El punto de control S2 se superó el 20 de octubre de 2026. Un clon arrancó en un mini PC real, con Windows en C: y WinRE habilitado. Consulta [docs/spikes/s2-arranque-hardware-real.md](docs/spikes/s2-arranque-hardware-real.md). Las validaciones manuales se registran en [docs/validacion-manual.md](docs/validacion-manual.md).

## Licencia

[MIT](LICENSE). Puedes usar, modificar y redistribuir el código citando la autoría. El software se ofrece "tal cual", sin garantía de ningún tipo: clonar un disco borra el disco de destino, así que guarda antes lo que necesites.

## Documentación

- [Decisiones iniciales](docs/decisiones-iniciales.md)
- [Glosario](docs/glosario.md)
- [Validación manual](docs/validacion-manual.md)
- [Pendientes para la versión 1.0](docs/pendientes-1.0.md)
