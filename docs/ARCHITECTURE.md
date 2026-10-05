# Arquitectura

Pequeña a propósito: dos proyectos de código, sin contenedor DI, sin framework MVVM, sin capas "enterprise".

```
┌──────────────────────────── ZipDrop (WPF, net9.0-windows) ─────────────────────────────┐
│                                                                                         │
│  App.xaml.cs  ── composition root: crea y conecta todo, aplica ajustes, menú bandeja    │
│     │                                                                                   │
│     ├─ UI/OverlayWindow + OverlayViewModel   cesta flotante (drop, estados, progreso)   │
│     ├─ UI/SettingsWindow                     5 ajustes, se aplican al instante          │
│     ├─ Services/ShakeService ─► MouseHook    WH_MOUSE_LL en hilo propio → ShakeDetector │
│     ├─ Services/GlobalHotkey                 RegisterHotKey (WM_HOTKEY)                 │
│     ├─ Services/TrayIcon                     Shell_NotifyIcon + ContextMenu WPF         │
│     ├─ Services/MessageWindow                HWND oculto para hotkey y bandeja          │
│     ├─ Services/SingleInstance               mutex + named pipe (reenvía rutas/“show”)  │
│     ├─ Services/StartupRegistration          HKCU\…\Run                                 │
│     ├─ Services/ThemeService                 claro/oscuro según Windows                 │
│     ├─ Services/ShellDragImage               IDropTargetHelper (imagen de arrastre)     │
│     ├─ Services/MemoryTrimmer                compacta memoria al quedar inactiva        │
│     └─ Interop/NativeMethods                 TODO el P/Invoke                           │
│                                                                                         │
└──────────────────────────────────────┬──────────────────────────────────────────────────┘
                                       │ referencia
┌──────────────────────────── ZipDrop.Core (net9.0, sin WPF) ─────────────────────────────┐
│  Baskets/Basket, BasketItem, PathNames      estado de la cesta, duplicados, missing     │
│  FileSystem/FolderScanner                   recorrido recursivo (sin seguir reparse pts)│
│  Archiving/ZipPlanner                       referencias → lista plana de entradas ZIP   │
│  Archiving/ZipBuilder, CompressionPolicy    escribe el ZIP (temp + move, progreso)      │
│  Gestures/ShakeDetector, ShakeOptions       reconocedor matemático de shake             │
│  Settings/AppSettings, SettingsStore        JSON en %APPDATA%\ZipDrop                   │
│  Formatting/SizeFormatter                                                               │
└─────────────────────────────────────────────────────────────────────────────────────────┘
```

Regla: **todo lo que se pueda testear sin Windows vive en `ZipDrop.Core`**. La app solo contiene UI e
integración con el sistema.

## Flujo principal

1. **Recopilar.** `OverlayWindow` recibe un drop OLE (`DataFormats.FileDrop`) → `OverlayViewModel.AddPaths`
   → `Basket.Add`. La cesta normaliza rutas, descarta duplicados exactos (case-insensitive) y guarda
   `BasketItem` (ruta, tipo, tamaño). Los tamaños de carpetas se miden en segundo plano
   (`Basket.MeasurePendingAsync`). Otras entradas: segunda instancia con rutas (*Enviar a*, CLI).
2. **Vigilar.** Mientras el overlay es visible, un `DispatcherTimer` (2 s) llama a
   `Basket.RefreshExistence` → los ítems desaparecidos se marcan `IsMissing` (y se desmarcan si vuelven).
3. **Crear ZIP.** `SaveFileDialog` nativo → `ZipBuilder.CreateAsync` en un hilo del pool:
   `ZipPlanner.Plan` (resuelve nombres/conflictos, excluye el propio destino, reporta missing) →
   `ZipBuilder.Build` escribe en `.<nombre>.<guid>.zipdrop-tmp` y hace `File.Move` atómico al final.
   El progreso llega a la UI con `IProgress<ZipProgress>` (throttle 50 ms). Cancelable.
4. **Resultado.** Panel "ZIP created" + *Open folder* (`explorer /select`). La cesta se vacía si el ajuste
   lo indica y no se omitió ningún archivo.

## Invocación del overlay

```
Explorer drag ─► MouseHook (hilo "ZipDrop mouse hook", GetMessage loop)
                   │  callback mínimo: ButtonDown/Up/Move → ShakeService.OnMouse
                   ▼
               ShakeService  ── ButtonDown: guarda punto; en ThreadPool: DPI del monitor + ¿origen shell?
                   │            Move: umbral de drag → ShakeDetector.Feed(x/escala, y/escala, t)
                   │            ButtonUp: reset + DragEnded
                   ▼ (evento en hilo del hook)
               App ─ Dispatcher.BeginInvoke ─► OverlayWindow.SummonForDrag(x, y)
```

- `Ctrl+Shift+Z` → `GlobalHotkey` (WM_HOTKEY en `MessageWindow`) → `OverlayWindow.Toggle`.
- Clic en bandeja → `Toggle`. Menú → *Open*, *New basket*, *Settings*, *Exit*.

### Comportamiento del overlay

- Ventana WPF transparente (layered), `Topmost`, `ShowActivated=false`, `WS_EX_TOOLWINDOW` (fuera de
  Alt+Tab y barra de tareas). Se oculta, nunca se cierra.
- Se coloca junto al cursor en **píxeles físicos** usando el DPI y el área de trabajo del monitor del
  cursor (`PlaceNear`), y se reajusta si crece o cambia de DPI (`EnsureOnScreen`).
- Si la invocó un shake: sin drop → se oculta 700 ms después de soltar; con drop → se recoge a los 3,5 s
  si el ratón no está encima. Si la invocó el usuario (hotkey/bandeja) o la movió, no se auto-oculta.

## Hilos

| Hilo | Qué hace |
|---|---|
| UI (STA, Dispatcher) | Ventanas, `Basket` (no es thread-safe: solo se toca aquí), hotkey, bandeja |
| Hook de ratón | `SetWindowsHookEx(WH_MOUSE_LL)` + bucle de mensajes. Callback trivial (el SO elimina hooks lentos) |
| ThreadPool | Inspección de ventana bajo el cursor, medición de carpetas, creación del ZIP, servidor del named pipe |

## Persistencia

- `%APPDATA%\ZipDrop\settings.json` — `AppSettings` (escritura atómica tmp + move).
- La cesta **no** se persiste entre reinicios (es temporal por diseño; ver ROADMAP).
- `%LOCALAPPDATA%\ZipDrop\error.log` y `trace.log` (este último solo con `ZIPDROP_TRACE=1`).

## Puntos de extensión previstos (no implementados)

- **Varias cestas:** `Basket` no tiene estado estático y tiene `Name`; bastaría una colección de cestas
  en `App` y un selector en el overlay. `OverlayViewModel` recibe la cesta por constructor.
- **ZIP existente como cesta:** `ZipBuilder` recibe un `ZipPlan`; un plan podría incluir entradas
  copiadas de un ZIP existente (`ZipArchiveMode.Update` o recomposición).
- **Contraseña / 7z / niveles:** `ZipBuilder` + `CompressionPolicy` son el único punto que escribe archivos.
- **Drag actions (DROP → Folder/Upload/Script):** el "destino" de la cesta es hoy `ZipBuilder`; una
  interfaz de acción reemplazaría la llamada en `OverlayViewModel.CreateZipAsync`.

## Tests

`tests/ZipDrop.Core.Tests` (xUnit): cesta (duplicados, missing, medición), planner (nombres, conflictos,
Unicode, destino dentro de carpeta), builder (criterio de éxito, cancelación, progreso, rutas largas,
compresión) y `ShakeDetector` con trayectorias de cursor sintéticas (sin hooks).

La UI no tiene tests automáticos; durante el desarrollo se verificó end-to-end con drags OLE reales
simulados (`DoDragDrop` + `mouse_event`) y UI Automation: [tools/e2e](../tools/e2e/README.md).
