# Decisiones técnicas

Registro de decisiones con su motivo. Añade nuevas al final con el siguiente número; si una decisión
cambia, no la borres: márcala como *Sustituida por D-0xx*.

---

## D-001 · Stack: C# + WPF sobre .NET 9 (preparado para .NET 10)

**Decisión.** C# + WPF. Target `net9.0-windows` porque en la máquina de desarrollo solo está instalado
el SDK 9.0.304. Migrar a .NET 10 es cambiar `TargetFramework` en los tres `.csproj` (no se usa nada
específico de la versión).

**Por qué WPF.** Integración Windows directa (HWND, P/Invoke, OLE drag & drop nativo, `RegisterHotKey`,
hooks), DPI per-monitor, ventanas transparentes con animaciones, ejecutable de ~0,5 MB
*framework-dependent*. Alternativas descartadas:
- *Electron/Tauri*: RAM y tamaño desproporcionados para una micro-utilidad; drag & drop nativo y hooks
  igualmente necesitarían código nativo.
- *WinUI 3*: overlay transparente sin barra de título, topmost y sin activación es más difícil y
  frágil; despliegue (Windows App SDK) más pesado.
- *Win32/C++ puro*: máximo control pero mucho más coste para una UI moderna y mantenible por agentes.

## D-002 · Núcleo separado sin WPF (`ZipDrop.Core`)

Cesta, plan ZIP, escritura ZIP, detector de shake y ajustes no dependen de WPF ni de Win32, para poder
testearlos con xUnit y con secuencias simuladas. La app WPF solo es UI + integración.

## D-003 · Sin DI container ni framework MVVM

`App.xaml.cs` es la raíz de composición y conecta ~10 objetos a mano. Un `Observable` y un
`RelayCommand` de 40 líneas sustituyen a CommunityToolkit. Menos dependencias, arranque más rápido.

## D-004 · Bandeja con `Shell_NotifyIcon` propio, no WinForms `NotifyIcon`

Evita cargar WinForms (memoria y conflictos de tipos con WPF) y permite un menú contextual WPF con el
mismo tema. Se re-añade el icono al recibir `TaskbarCreated` (reinicio de Explorer). Por eso la ventana
de mensajes es top-level oculta y no `HWND_MESSAGE` (estas no reciben broadcasts).

## D-005 · La cesta guarda referencias, nunca copias

`BasketItem` = ruta normalizada + tipo (archivo/carpeta) + tamaño. Nada se copia hasta crear el ZIP.
El tamaño de las carpetas se mide en segundo plano y se muestra como aproximado (`143 MB…` mientras mide).

**Duplicados:** misma ruta exacta (normalizada con `Path.GetFullPath`, sin separador final,
comparación `OrdinalIgnoreCase`) → se ignora y se avisa ("1 already in basket"). Un archivo que está
*dentro* de una carpeta ya añadida **sí** se añade (el usuario lo pidió explícitamente): aparecerá en la
raíz del ZIP y dentro de la carpeta.

## D-006 · Nombres dentro del ZIP y conflictos

- Cada ítem es una entrada de primer nivel con su nombre: `foto.jpg`, `proyecto/…`.
- Separador `/`, nombres en UTF-8 (flag de idioma en el ZIP, lo hace `System.IO.Compression`).
- Comparación case-insensitive (la extracción en Windows lo es).
- Si dos ítems producen el mismo nombre de primer nivel, el posterior se renombra
  `nombre (2).ext`, `nombre (3).ext`… (para carpetas `proyecto (2)/`). Un archivo y una carpeta con el
  mismo nombre también se consideran conflicto.
- Dentro de una carpeta la estructura no se toca. Red de seguridad para directorios case-sensitive
  (`a.txt` y `A.txt`): se renombra la hoja.
- **Nunca** se sobrescribe una entrada. Los renombrados se informan en el resultado.
- El propio ZIP de destino (y su temporal) se excluye si se guarda dentro de una carpeta añadida.
- Las carpetas vacías se conservan (entrada `carpeta/`).

## D-007 · Escritura del ZIP

- `System.IO.Compression.ZipArchive` (Zip64 automático para > 4 GB, UTF-8). Sin dependencias externas.
- Se escribe a `.<nombre>.<guid>.zipdrop-tmp` (oculto) en la carpeta destino y se mueve al final:
  cancelar o fallar nunca deja un ZIP roto ni destruye un archivo existente que se iba a sobrescribir.
- El archivo origen se abre **antes** de crear su entrada (`FileShare.ReadWrite|Delete`): si está
  bloqueado o ya no existe se omite limpiamente y se informa ("N file(s) skipped").
- Formatos ya comprimidos (jpg, png, mp4, zip, docx…) se guardan sin compresión (`CompressionPolicy`):
  mucho más rápido con archivos grandes y apenas cambia el tamaño. Resto: `Optimal`.
- No se siguen *reparse points* de directorio (symlinks/junctions): evita ciclos y contenido ajeno.
- Fechas fuera del rango ZIP (1980–2107) se acotan.
- Rutas largas: .NET (Core) las soporta sin prefijo `\\?\`; además `longPathAware` en el manifiesto.

## D-008 · Efecto de drop: Copy (o Link), nunca Move

Si el destino devuelve `DROPEFFECT_MOVE`, el origen (Explorer) puede borrar los originales. ZipDrop
devuelve `Copy` si está permitido, si no `Link`. Los archivos nunca se tocan.

## D-009 · Gesto shake

**Investigación.** Windows no ofrece ninguna API para saber si hay un drag OLE en curso en otro proceso,
ni para observar sus datos. Opciones evaluadas:
1. *Ventana invisible a pantalla completa registrada como drop target* para detectar `DragEnter`: hack
   frágil (roba hit-testing, interfiere con el escritorio, problemas con varios monitores y DPI). Descartada.
2. *Sondeo con `GetAsyncKeyState` + `GetCursorPos`*: consume CPU constantemente y pierde muestras. Descartada.
3. **`WH_MOUSE_LL` (hook de bajo nivel)**: recibe todo el movimiento del ratón incluso durante el bucle
   modal de `DoDragDrop` de Explorer; no inyecta DLLs; es lo que usa Tokri en Windows. **Elegida.**

**Implementación.**
- Hook en un hilo dedicado con su propio `GetMessage` loop; el callback solo enruta eventos (el SO
  elimina silenciosamente hooks que superan `LowLevelHooksTimeout`). Se desinstala si el shake se desactiva.
- *Drag inferido*: botón (izq. o der.) pulsado + desplazamiento > `DragStartDistance` (10 px lógicos).
- *Filtro de origen*: al pulsar, se mira en el ThreadPool la ventana bajo el cursor y sus ancestros; solo
  cuentan vistas de archivos del shell (`CabinetWClass`, `SHELLDLL_DefView`, `Progman`, `WorkerW`…).
  Elimina los falsos positivos de Tokri (seleccionar texto, mover ventanas, dibujar). Desactivable para
  desarrollo con `ZIPDROP_SHAKE_ANY_SOURCE=1`.
- *Detector puro* (`ShakeDetector`) en coordenadas lógicas (se divide por la escala DPI del monitor):
  divide el movimiento horizontal en *swings* en cada inversión (ignorando retrocesos < `ReversalTolerance`).
  Hay shake cuando hay `RequiredSwings` swings consecutivos, cada uno ≥ `MinSwingDistance`, con velocidad
  media ≥ `MinSwingSpeed`, recorrido vertical ≤ `MaxVerticalRatio`·horizontal, todos dentro de
  `TimeWindowMs`. El swing en curso cuenta en cuanto cumple, así responde a mitad del gesto. Tras
  disparar, `CooldownMs`. Un único disparo por drag.
- Valores por defecto (Medium): 4 swings de 40 px, 250 px/s, ventana 750 ms, tolerancia 8 px, ratio
  vertical 0,8, cooldown 1200 ms. Low/High en `ShakeOptions.ForSensitivity`.

**Alternativa robusta.** El hotkey global y el icono de bandeja funcionan siempre, desde cualquier app.

## D-010 · Overlay

- Ventana WPF `AllowsTransparency` (layered) para esquinas redondeadas y sombra propias en Windows 10 y
  11. La sombra va en un elemento hermano para no perder ClearType en el texto.
- `ShowActivated=false` + `SWP_NOACTIVATE`: aparecer no roba el foco. Hacer clic sí la activa (intencional).
- No se usa `WS_EX_NOACTIVATE`: impediría el teclado (Esc) y complica el `SaveFileDialog` con owner.
- Auto-ocultado solo cuando la invocó un shake (ver ARCHITECTURE).

## D-011 · Hotkey global con `RegisterHotKey`

Sin hook de teclado (más seguro, sin latencia). `MOD_NOREPEAT`. Si la combinación está ocupada se avisa
(notificación de bandeja / error en ajustes) y se mantiene la anterior. Durante la captura de una nueva
combinación en Ajustes se desregistra temporalmente la actual. Por defecto `Ctrl+Shift+Z` (pedido en el
brief) — ver KNOWN_ISSUES: coincide con *Rehacer* en muchas apps.

## D-012 · Instancia única + named pipe + rutas por línea de comandos

Mutex por sesión/usuario. Una segunda ejecución envía sus argumentos por un named pipe
(`PipeOptions.CurrentUserOnly`) y termina. `ZipDrop.exe` sin argumentos muestra la cesta; con rutas las
añade. Esto da gratis la integración *Enviar a → ZipDrop* (el instalador crea el acceso directo) y
permite automatizar pruebas.

## D-013 · Ajustes

JSON en `%APPDATA%`, escritura atómica, carga tolerante (si está corrupto → valores por defecto). Solo
los 5 ajustes del MVP. Se aplican al instante (sin botón Guardar). *Launch at startup* usa
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` con `--background` (arranca oculta en bandeja).

## D-014 · Memoria en reposo

WPF ronda ~100 MB de working set tras arrancar. Como ZipDrop pasa casi todo el tiempo en la bandeja,
`MemoryTrimmer` compacta el GC y llama a `EmptyWorkingSet` 4 s después de quedar inactiva (arranque en
segundo plano u ocultar el overlay). Medido: ~4 MB de working set en reposo (Release, R2R).

## D-015 · Nunca elevar

Manifiesto `asInvoker`; instalador por usuario (`PrivilegesRequired=lowest`) en
`%LOCALAPPDATA%\Programs\ZipDrop`. Un ZipDrop elevado no puede recibir drops de un Explorer normal (UIPI).

## D-016 · Archivos que desaparecen

Se comprueban al mostrar la cesta, cada 2 s mientras es visible y justo antes de crear el ZIP. Un ítem
desaparecido se queda en la lista marcado (no se borra solo, para que el usuario vea cuál es), se puede
quitar individualmente o con *Remove missing items*, y se omite al crear el ZIP (informándolo). Si
todos faltan, *Create ZIP* se deshabilita.

## D-017 · Vaciar tras crear el ZIP

Con el ajuste activo (por defecto) la cesta se vacía tras un ZIP correcto, **salvo** que se haya omitido
algún archivo (bloqueado/sin acceso): así el usuario puede reintentar sin volver a recopilar.

## D-018 · Publicación

`build.ps1`: tests → `dotnet publish` Release win-x64 con ReadyToRun (arranque ~0,3 s).
Por defecto *framework-dependent* (~0,5 MB, requiere .NET Desktop Runtime); `-SelfContained` produce un
exe único comprimido (~64 MB) sin requisitos. Instalador con Inno Setup 6 si está instalado.

## D-019 · Iconos reales del sistema en la cesta

La cesta muestra los iconos de tipo de archivo de Windows (los del Explorador): una pila "en abanico" de
los 3 últimos ítems y un icono por fila en la lista. `ShellIcons` usa `SHGetFileInfo` con
`SHGFI_USEFILEATTRIBUTES` (no toca el disco: rápido y funciona con archivos *missing*) +
`SHGetImageList` (48 px) y cachea por extensión. Consecuencia: los iconos dependen de las asociaciones
de archivo del usuario (ver KNOWN_ISSUES).

## D-020 · Open source (MIT) y distribución

Gratis y open source: ZipDrop instala un hook global de ratón, así que poder auditar el código es parte
de la confianza del producto. Releases automáticas al subir un tag `v*` (`.github/workflows/release.yml`):
instalador Inno Setup, exe portable (self-contained) y build framework-dependent, con `SHA256SUMS.txt`.
CI compila con `-warnaserror` y ejecuta los tests en cada push/PR.

Material gráfico reproducible en `tools/media` (capturas con fondo limpio, imagen social, GIF de demo
grabado con el Explorador real). La demo deja **fuera del encuadre** el panel de navegación del
Explorador y no graba el diálogo de guardar, porque muestran carpetas y nombre del usuario.
