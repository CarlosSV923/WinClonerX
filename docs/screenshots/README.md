# Capturas de pantalla

Las capturas muestran la interfaz real de WinClonerX con datos ficticios: modelos y números de serie inventados, y un disco de origen con particiones EFI, MSR, Windows y Recovery. No se leen los discos de este equipo ni se ejecuta un clonado.

Para regenerar las doce imágenes PNG desde la raíz del repositorio, ejecuta:

```powershell
dotnet run --project tools/WinClonerX.Screenshots -c Release -- --out docs/screenshots
```

Vuelve a generar las capturas después de cambiar la interfaz para que las imágenes de los README sigan representando la aplicación.
