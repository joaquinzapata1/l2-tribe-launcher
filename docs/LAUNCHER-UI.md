# Personalizar la UI del launcher

El launcher usa Windows Forms y construye la interfaz por codigo. No tiene HTML
ni un editor visual drag-and-drop. El branding esta separado de la logica para
que puedas iterar sin tocar descargas, checksums o instalacion.

## Live preview

Hacer doble clic en `Live Preview Launcher.cmd`. Mientras esa consola siga
abierta, cada guardado en los archivos `.cs`, el proyecto o `Assets/` recompila
y reinicia automaticamente la ventana de preview.

WinForms no puede reacomodar de forma segura todos los controles existentes en
memoria. Por eso el refresh reinicia solamente la ventana, normalmente en pocos
segundos. Para un preview unico sin watcher, usar `Preview Launcher.cmd`.

## Preview unico

Hacer doble clic en `Preview Launcher.cmd`. El script compila y abre la ventana;
si falla, deja el error visible. Desde terminal tambien podes ejecutar:

```powershell
.\preview.ps1
```

Cada vez que cambies algo, cerra el preview y volve a abrirlo.

## Colores, textos y redes

Editar `LauncherBranding.cs`:

- `Canvas`, `Surface` y `SurfaceRaised`: fondos.
- `Accent`: CTA principal, progreso y detalles destacados.
- `Text` y `MutedText`: jerarquia tipografica.
- `HeroTitle`, `HeroDescription` y textos de cabecera: copy visible.
- `WebsiteUrl`, `DiscordUrl`, `InstagramUrl`, `FacebookUrl` y `TwitchUrl`:
  destinos oficiales para la barra de redes.

Los colores aceptan hexadecimal CSS como `#EBAA39`.

## Logo y hero

Reemplazar, conservando los nombres:

- `Assets/l2-hamburgo-logo.png`: logo transparente; actual 1920x716.
- `Assets/launcher-hero.jpg`: hero actual 1920x1200, mostrado con recorte `cover`.

Conviene mantener el hero entre 1600 y 2560 px de ancho, JPG calidad 85-90, y
dejar espacio visual oscuro donde aparezca el texto.

## Layout y controles

Editar `BuildLayout()` en `MainForm.cs` para mover bloques o cambiar tamanos:

1. `header`: logo, redes, estado de build y controles de ventana.
2. `hero`: imagen, copy y CTA dinamico Instalar/Actualizar/Jugar.
3. `footer`: carpeta, progreso y acciones secundarias.

No hace falta tocar `ContentInstaller.cs`, `PackageInstaller.cs` ni
`GitHubReleaseClient.cs` para trabajar el aspecto visual.

## Direccion similar a Reborn

- conservar un unico CTA fuerte: Instalar, Actualizar o Jugar;
- mover Reparar, buscar updates y carpeta a un menu de configuracion;
- usar una barra superior con Discord, Instagram, Facebook y Twitch;
- hacer que el hero ocupe la mayor parte de la ventana;
- mantener contraste alto, bordes suaves y poco texto operativo;
- usar una familia display para titulos y otra neutral para informacion.

## Build final

Cuando el preview quede aprobado:

```powershell
.\build.ps1
```

El EXE y su checksum quedan en `build/win-x64/`.
