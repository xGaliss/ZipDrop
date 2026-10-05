# ZipDrop

**Build ZIPs while you work.**

ZipDrop es una micro-utilidad para Windows: recopilas archivos y carpetas desde cualquier sitio en una
cesta flotante y, cuando terminas, pulsas **Create ZIP**. Sin carpetas temporales, sin copiar nada,
sin abrir WinRAR/7-Zip.

```
FILES → SHAKE → DROP → CONTINUE WORKING → MORE FILES → DROP → CREATE ZIP
```

ZipDrop **no** es un gestor de archivos ni un compresor completo. Si una función no mejora ese flujo,
no está aquí.

## Uso

| Acción | Cómo |
|---|---|
| Abrir la cesta mientras arrastras archivos | Arrastra desde el Explorador o el escritorio y **agita el ratón izquierda-derecha** (← → ← →) |
| Abrir / ocultar la cesta | **Ctrl + Shift + Z** (configurable) o clic en el icono de la bandeja |
| Añadir archivos | Suéltalos sobre la cesta. También: *Enviar a → ZipDrop* (si lo instalaste) o `ZipDrop.exe <rutas…>` |
| Ver qué contiene / qué falta | Botón **Items** o el aviso **N missing** |
| Crear el ZIP | **Create ZIP** → eliges nombre y carpeta (sugerido `Archive.zip`) |
| Vaciar | **Clear** (pide un segundo clic para evitar borrados accidentales) |

La cesta guarda **referencias** a las rutas originales: nada se copia hasta que creas el ZIP.
Si un archivo se mueve o se borra entretanto, la cesta lo marca como *missing* y el ZIP se crea sin él
(indicándolo).

Menú de la bandeja: *Open ZipDrop · New basket · Settings · Exit*. Cerrar la cesta (×, Esc) no cierra la app.

## Requisitos

- Windows 10 1809+ / Windows 11, x64.
- Para ejecutar la build *framework-dependent*: [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
  La build *self-contained* no necesita nada.
- Para desarrollar: .NET 9 SDK (ver [D-001](docs/DECISIONS.md) sobre .NET 10).

## Desarrollo

```powershell
dotnet build                                   # compila todo
dotnet test                                    # tests del núcleo (cesta, ZIP, shake…)
dotnet run --project src/ZipDrop               # ejecuta la app
./build.ps1                                    # tests + publish Release en artifacts/publish (+ instalador si hay Inno Setup)
./build.ps1 -SelfContained                     # exe único con el runtime embebido
powershell -File tools/make-icon.ps1           # regenera Assets/ZipDrop.ico
```

Variables de entorno útiles en desarrollo (desactivadas por defecto):

| Variable | Efecto |
|---|---|
| `ZIPDROP_TRACE=1` | Escribe eventos (drag/drop, shake, hook, ajustes) en `%LOCALAPPDATA%\ZipDrop\trace.log` |
| `ZIPDROP_SHAKE_ANY_SOURCE=1` | Permite el shake desde cualquier aplicación, no solo Explorer/escritorio |

Los parámetros del gesto (distancia, nº de cambios de dirección, velocidad, ventana temporal, cooldown…)
están centralizados en [`ShakeOptions`](src/ZipDrop.Core/Gestures/ShakeOptions.cs).

## Estructura

```
src/ZipDrop.Core/        Lógica pura, sin WPF (testeable): cesta, plan/creación de ZIP, detector de shake, ajustes
src/ZipDrop/             App WPF: overlay, ajustes, bandeja, hotkey, hook de ratón, interop Win32
tests/ZipDrop.Core.Tests xUnit
installer/               Script de Inno Setup (instalación por usuario, sin admin)
tools/                   Utilidades (generador de icono)
docs/                    Documentación viva — léela antes de cambiar nada
```

## Documentación

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — módulos, hilos, flujo de datos
- [docs/DECISIONS.md](docs/DECISIONS.md) — decisiones técnicas y su motivo
- [docs/ROADMAP.md](docs/ROADMAP.md) — fases, estado y futuro
- [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md) — limitaciones conocidas (drag & drop, hooks, shake, DPI…)

## Privacidad

Todo ocurre en tu equipo. Sin cuentas, sin login, sin nube, sin backend, sin telemetría, sin analytics.
Los únicos archivos que escribe ZipDrop son tus ZIP, `%APPDATA%\ZipDrop\settings.json` y, si algo falla,
`%LOCALAPPDATA%\ZipDrop\error.log` (local, nunca se envía).
