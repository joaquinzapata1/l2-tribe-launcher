# Personalizar la UI del launcher

El launcher usa Windows Forms y construye la interfaz por codigo. No tiene HTML
ni un editor visual drag-and-drop. El branding esta separado de la logica para
que puedas iterar sin tocar descargas, checksums o instalacion.

## Live preview

Hacer doble clic en `Live Preview Launcher.cmd`. Mientras esa consola siga
abierta, cada guardado en los archivos `.cs`, el proyecto o `Assets/` recompila
y reinicia automaticamente la ventana de preview. Este modo usa `dotnet run`
para evitar que Windows Smart App Control bloquee un `.exe` nuevo en cada cambio.

WinForms no puede reacomodar de forma segura todos los controles existentes en
memoria. Por eso el refresh reinicia solamente la ventana, normalmente en pocos
segundos. Para un preview unico sin watcher, usar `Preview Launcher.cmd`.

## Preview unico

Hacer doble clic en `Preview Launcher.cmd`. El script compila y abre la ventana;
si falla, deja el error visible. Desde terminal tambien podes ejecutar:

```powershell
.\dev-preview.ps1
```

Cada vez que cambies algo, cerra el preview y volve a abrirlo.

`preview.ps1` sigue existiendo para probar el `.exe` single-file publicado en
`%LOCALAPPDATA%`, pero para iterar UI conviene usar `dev-preview.ps1` o
`Live Preview Launcher.cmd`.

## Colores, textos y redes

Editar `LauncherBranding.cs`:

- `Canvas`, `Surface` y `SurfaceRaised`: fondos.
- `Accent`: CTA principal, progreso y detalles destacados.
- `Text` y `MutedText`: jerarquia tipografica.
- `Chronicle` y `Rate`: cronica y rates visibles en el hero.
- `WebsiteUrl`, `DiscordUrl`, `InstagramUrl`, `FacebookUrl` y `TwitchUrl`:
  destinos oficiales para la barra de redes.
- `DiscordServerId`: guild ID usado por futuras integraciones de Discord; el
  boton visible usa `DiscordUrl` para que tambien puedan entrar usuarios nuevos.

Los colores aceptan hexadecimal CSS como `#EBAA39`.

## Logo y hero

La marca del header se muestra como imagen desde `Assets/l2tribe-logo.png`;
`LauncherBranding.LogoResource` apunta al recurso embebido. Reemplazá ese
archivo si querés cambiar el logo.

- `Assets/l2tribe-logo.png`: logo actual mostrado arriba a la izquierda.
- `Assets/launcher-hero.jpg`: hero actual 1920x1200, mostrado con recorte `cover`.

Conviene mantener el hero entre 1600 y 2560 px de ancho, JPG calidad 85-90, y
dejar espacio visual oscuro donde aparezca el texto.

## Layout y controles

Editar `BuildLayout()` en `MainForm.cs` para mover bloques o cambiar tamanos:

1. `header`: wordmark, Discord, idioma y controles de ventana.
2. `hero`: imagen, version y CTA dinamico Instalar/Actualizar/Jugar.
3. `footer`: progreso y menu secundario `...`.

El selector `ES / EN / PT` usa `LauncherLocalization.cs`. Los textos visibles
deben agregarse ahi en los tres idiomas, no directamente en `MainForm.cs`.

La pantalla principal mantiene un solo CTA dinamico. Cambiar carpeta y verificar
archivos viven en `OPCIONES`; la consulta de updates se ejecuta automaticamente
al abrir el launcher.

El menu `OPCIONES` contiene solamente acciones para jugadores:

- `Cambiar carpeta del cliente`: raiz que contiene `system-e`, o carpeta vacia
  para una instalacion nueva.
- `Verificar archivos`: compara el cliente instalado contra el manifiesto y
  vuelve a descargar archivos faltantes o corruptos.

No hace falta tocar `ContentInstaller.cs`, `PackageInstaller.cs` ni
`GitHubReleaseClient.cs` para trabajar el aspecto visual.

## Direccion similar a Reborn

- conservar un unico CTA fuerte: Instalar, Actualizar o Jugar;
- mover Reparar, buscar updates y carpeta a un menu de configuracion;
- usar una barra superior simple con Discord;
- hacer que el hero ocupe la mayor parte de la ventana;
- mantener contraste alto, bordes suaves y poco texto operativo;
- usar una familia display para titulos y otra neutral para informacion.

## Build final

Cuando el preview quede aprobado:

```powershell
.\build.ps1
```

El EXE y su checksum quedan en `build/win-x64/`.
