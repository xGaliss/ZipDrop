# Pruebas end-to-end manuales asistidas

Scripts para Windows PowerShell 5.1 que **mueven el ratón real y pulsan botones**: no los ejecutes
mientras usas el equipo. Coordenadas en píxeles físicos.

| Script | Qué hace |
|---|---|
| `screenshot.ps1 -Name x [-Title "ZipDrop Settings"]` | Captura la ventana visible de ZipDrop con ese título a `x.png` junto al script e imprime su rectángulo |
| `drag-drop.ps1 -Files "a\|b" -TargetX -TargetY [-SourceX -SourceY] [-Shake]` | Inicia un drag OLE real (`FileDrop`, efectos Copy/Move/Link) desde un formulario temporal y suelta en el destino. `-Shake` agita a mitad de camino. Siempre suelta el botón (watchdog de 15 s) |
| `tray-menu.ps1 [-Pick n] [-Screenshot x.png]` | Abre el panel de iconos ocultos si hace falta, hace clic derecho real en el icono de ZipDrop y comprueba que el menú sigue abierto (exit 1 si no). `-Pick` elige la opción n |
| `drag-out.ps1` | Con la cesta en "ZIP created": arrastra el ZIP a una carpeta vacía del Explorador y comprueba que llegó la copia |
| `drag-out-browser.ps1` | Igual, pero a una página local en Edge (`browser-drop.html`) que muestra en el título el archivo recibido. Cierra solo su ventana (por UIA, nunca con teclado) |
| `click-button.ps1 [-ButtonName CreateZip] [-Destination ruta.zip]` | Pulsa un botón del overlay por UI Automation: acepta el `AutomationId` estable (`CreateZip`, `ShowItems`, `Settings`, `Hide`, `CopyZip`, `OpenFolder`, `Done`…) o el nombre visible. Con `-Destination` espera el diálogo *Save ZIP* (si no rellena el nombre, escribir con SendKeys: el campo tiene el foco) |

Ejemplo (shake desde una app que no es Explorer, por eso la variable):

```powershell
$env:ZIPDROP_TRACE = "1"; $env:ZIPDROP_SHAKE_ANY_SOURCE = "1"
dotnet run --project src/ZipDrop -- --background
powershell -STA -File tools/e2e/drag-drop.ps1 -Files "C:\tmp\a.txt" -SourceX 400 -SourceY 760 -TargetX 760 -TargetY 760 -Shake
Get-Content $env:LOCALAPPDATA\ZipDrop\trace.log
```

`-Files` es un único string separado por `|` (con `powershell -File` los arrays llegan como un string).
