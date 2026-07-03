# L2 Hamburgo Launcher

Codigo fuente y distribucion publica del instalador, updater y launcher de
L2 Hamburgo para Windows.

## Descargar para jugar

Abrir [Releases](https://github.com/joaquinzapata1/l2-client-updater/releases),
elegir la ultima version y descargar solamente `InterludeLauncher.exe`.

El launcher instala el cliente completo, aplica actualizaciones incrementales,
repara archivos por SHA-256 y crea un acceso directo `L2 Hamburgo` en el
escritorio. El boton Jugar inicia tambien Discord Rich Presence.

El EXE todavia no esta firmado digitalmente; Windows puede mostrar una
advertencia de editor desconocido durante esta etapa de pruebas.

## Abrir la UI para editarla

Para trabajar con refresh automatico, hacer doble clic en:

```text
Live Preview Launcher.cmd
```

Cada vez que guardes un archivo de estilos, layout o imagen, el launcher se
recompila y vuelve a abrir solo. La consola queda visible para mostrar errores.

Para abrir una unica vez, hacer doble clic en:

Hacer doble clic en:

```text
Preview Launcher.cmd
```

Si algo falla, el CMD conserva el error en pantalla. Alternativamente:

```powershell
.\preview.ps1
```

La guia de colores, textos, assets y layout esta en
[`docs/LAUNCHER-UI.md`](docs/LAUNCHER-UI.md).

## Build distribuible

```powershell
.\build.ps1
```

Output:

```text
build/win-x64/InterludeLauncher.exe
build/win-x64/InterludeLauncher.exe.sha256
```

El build es self-contained y no requiere .NET instalado en la PC del jugador.
Para desarrollo, los scripts usan `dotnet` del PATH o el SDK local del repo
hermano `l2classic-interlude-custom`.

## Funcionalidad

- instala el cliente completo en una carpeta elegida por el jugador;
- consulta las Releases `client-v*` de este repositorio;
- descarga paquetes inmutables en paralelo;
- actualiza solamente los archivos modificados;
- valida paquetes y archivos con SHA-256;
- respalda, revierte y permite cancelar operaciones;
- repara archivos faltantes o corruptos;
- instala una copia persistente del launcher y su acceso directo;
- inicia `system-e/l2.exe` y el companion de Discord.

## Repositorios

- Este repo es la fuente de verdad del launcher y su UI.
- `joaquinzapata1/l2classic-interlude-custom` genera y publica los paquetes de
  contenido que consume el launcher.

## Licencia y marca

El codigo esta disponible bajo la licencia MIT. El nombre, logo y assets de
L2 Hamburgo no se licencian para reutilizacion; ver `TRADEMARKS.md`.
