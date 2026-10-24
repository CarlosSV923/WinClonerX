# Diagramas de arquitectura

La web está en español y publica la versión inglesa en `site/en/`. Los candidatos JSON son la fuente editable; cada pareja conserva la misma estructura. La recuperación de un diario de restauración pendiente ocurre al iniciar el asistente, antes de crear una ejecución, por eso se documenta aquí y no como estado de `CloneExecution`.

- **Arquitectura:** el recorrido del usuario por WPF, Application, Domain e Infrastructure.Windows hasta Windows y los discos; destaca el límite `DiskWriteGuard`.
- **Flujo de clonado:** fases condicionales, decisiones, verificación, operaciones no interrumpibles y compensaciones.
- **Ciclo de vida:** estados de `CloneExecution`, cancelación diferida, verificación aprobada, retirada de dispositivo y fallos.

Se requiere Node.js 24 y Archify 3.2.0 instalado en `C:\Users\carlo\.claude\skills\archify`. Desde la raíz del repositorio, regenera cada idioma con:

```powershell
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize architecture docs/architecture/architecture.es.json docs/architecture/site/architecture.html --repo-root . --quality showcase --json
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize architecture docs/architecture/architecture.en.json docs/architecture/site/en/architecture.html --repo-root . --quality showcase --json
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize workflow docs/architecture/clone-workflow.es.json docs/architecture/site/clone-workflow.html --repo-root . --quality showcase --json
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize workflow docs/architecture/clone-workflow.en.json docs/architecture/site/en/clone-workflow.html --repo-root . --quality showcase --json
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize lifecycle docs/architecture/execution-lifecycle.es.json docs/architecture/site/execution-lifecycle.html --repo-root . --quality showcase --json
node C:\Users\carlo\.claude\skills\archify\bin\archify.mjs finalize lifecycle docs/architecture/execution-lifecycle.en.json docs/architecture/site/en/execution-lifecycle.html --repo-root . --quality showcase --json
```

Cada candidato fija el commit usado por las citas. Al actualizar las fuentes o sus líneas, actualiza `meta.repository.revision`, revisa las citas y vuelve a finalizar los diagramas afectados. GitHub Pages publica directamente `site/`; el índice no depende de CDN.
