# Problemas y limitaciones conocidas

Mantener actualizado. Cada entrada: síntoma → causa → mitigación/estado.

## Drag & drop desde Windows Explorer

- **No hay detección "real" de drag global.** Windows no expone si otro proceso está en un bucle
  `DoDragDrop` ni qué arrastra. ZipDrop lo infiere (botón pulsado + movimiento) — ver D-009.
- **Elevación (UIPI).** Si ZipDrop se ejecuta como administrador, Explorer (no elevado) no puede soltar
  sobre él: el cursor muestra 🚫. *Mitigación:* manifiesto `asInvoker` e instalador por usuario. No lo
  ejecutes "como administrador". A la inversa, arrastrar desde una app elevada tampoco funciona.
- **Archivos virtuales no soportados**: adjuntos arrastrados desde Outlook, elementos dentro de un ZIP
  abierto en Explorer, dispositivos MTP (móviles). Llegan como `FileGroupDescriptorW`, no `FileDrop`;
  el overlay no los acepta. Roadmap.
- **Imagen de arrastre.** Se usa `IDropTargetHelper` para mantener la miniatura de Explorer sobre el
  overlay. Es best-effort: si falla, solo se pierde la imagen, no el drop. El texto del badge es el de
  Windows ("Copiar"), no "Add to ZipDrop" (roadmap: `DropDescription`).
- El efecto devuelto es *Copy* (o *Link*): Explorer nunca borra los originales (D-008).

## Hooks globales de ratón (`WH_MOUSE_LL`)

- Windows **elimina silenciosamente** un hook LL cuyo callback tarda más que `LowLevelHooksTimeout`
  (~1 s; en Windows 7+ sin aviso). *Mitigación:* hilo dedicado, callback trivial, trabajo pesado en el
  ThreadPool. Si el sistema se congela (depurador en un breakpoint, máquina saturada) el shake puede dejar
  de funcionar hasta reiniciar ZipDrop o reactivar el ajuste. El hotkey sigue funcionando.
- Un proceso no elevado **no recibe** input dirigido a ventanas elevadas: no habrá shake sobre apps admin.
- Algunos antivirus/EDR marcan heurísticamente los hooks globales. Firmar el binario ayuda (roadmap).
- En sesiones RDP/escritorio remoto y con algunas tabletas/lápices el patrón de eventos puede variar.
- Movimientos sintéticos con `SetCursorPos` no generan eventos LL; `SendInput`/`mouse_event` con
  movimiento real sí (relevante para pruebas automatizadas).

## Gesto shake

- **Heurístico.** Puede no dispararse con gestos muy lentos/cortos (sube la sensibilidad) o dispararse
  con un vaivén enérgico dentro de Explorer **sin** arrastrar archivos, p. ej. durante una selección
  rectangular (*rubber band*), que también empieza en `SHELLDLL_DefView`. *Mitigación:* si no se suelta
  nada, el overlay se oculta solo 700 ms después de soltar el botón.
- **Solo desde Explorer/escritorio/diálogos de archivos** por defecto. Arrastres desde otras apps
  (VS Code, navegador, Total Commander…) → usar el hotkey o el icono de bandeja. En desarrollo:
  `ZIPDROP_SHAKE_ANY_SOURCE=1`.
- El hook ve el *button-up* **antes** de que Explorer entregue el `Drop` al overlay; por eso la decisión
  de auto-ocultar se toma con un pequeño retraso.
- Verificado con el Explorador real de Windows 11 (drags desde vista de iconos y de detalles, varias
  carpetas) con movimiento generado por `mouse_event`. Falta calibración con usuarios reales y
  ratones/touchpads distintos.

## DPI

- Proceso *Per-Monitor V2* (manifiesto). El hook entrega píxeles físicos; el detector trabaja en
  píxeles lógicos dividiendo por la escala del monitor donde empezó el drag.
- Al mostrar el overlay en un monitor con DPI distinto al anterior, WPF reescala tras `WM_DPICHANGED`;
  el primer cálculo de posición usa el tamaño previo y luego se corrige con `EnsureOnScreen`. Puede verse
  un ajuste mínimo de posición en configuraciones de DPI mixto. Pendiente de verificación manual.

## Overlay

- `AllowsTransparency` usa ventanas *layered* con renderizado por software de WPF: más CPU que una
  ventana normal al animar. Con un overlay de 250 px es irrelevante, pero evita animaciones grandes.
- Los píxeles totalmente transparentes (margen de la sombra) no reciben clics ni drops (comportamiento
  esperado de las ventanas layered).
- `Topmost` no se muestra sobre aplicaciones en pantalla completa exclusiva ni sobre el escritorio seguro
  (UAC). Otra ventana topmost puede taparlo.
- Hacer clic en el overlay lo activa (roba el foco en ese momento). Aparecer, no (D-010).
- Esc solo funciona cuando el overlay tiene el foco.

## Hotkey

- **`Ctrl+Shift+Z` es *Rehacer* en muchas apps** (Photoshop, Figma, VS Code, navegadores…). Mientras
  ZipDrop está en marcha, `RegisterHotKey` se queda esa combinación en todo el sistema. Se mantuvo
  porque lo pide el brief; considerar cambiar el valor por defecto (p. ej. `Ctrl+Alt+Z` o `Win+Shift+Z`).
- Si otra app ya registró la combinación, ZipDrop avisa y sigue sin hotkey hasta que se cambie en Ajustes.

## Iconos

- Los iconos de la cesta son los de las asociaciones de archivo del usuario: si otra app se ha apropiado
  de una extensión (p. ej. `.docx`), la cesta muestra su icono, igual que el Explorador.
- Ejecutables, accesos directos e `.ico` muestran el icono genérico de su tipo, no el propio (no se lee
  el archivo, D-019).

## Distribución

- Los binarios no están firmados: SmartScreen puede avisar en la primera ejecución (*Más información →
  Ejecutar de todas formas*). Firmar está en el roadmap.

## Permisos / sistema

- *Launch at startup* escribe en `HKCU\…\Run`. Con políticas de grupo que bloqueen el registro, el ajuste
  no tiene efecto (sin error visible).
- El menú de bandeja es el menú nativo de Win32. El modo oscuro usa APIs no documentadas de uxtheme;
  en versiones de Windows donde no existan, el menú se verá claro.
- *Resuelto en v0.1.1:* en v0.1.0 el menú de bandeja desaparecía al instante si el icono estaba en el
  panel de iconos ocultos (D-004).

## ZIP

- Archivos bloqueados en exclusiva por otro proceso se omiten y se informa; la cesta no se vacía (D-017).
- El tamaño mostrado es la suma de originales (aproximado); el ZIP final puede ser menor o casi igual
  (formatos ya comprimidos se almacenan, D-007).
- Las carpetas se recorren en el momento de crear el ZIP: lo añadido/borrado dentro de una carpeta
  después de soltarla sí se refleja.
- No se siguen symlinks/junctions de directorio dentro de carpetas añadidas (se incluye la entrada,
  no su contenido).

## Pruebas

- La UI no tiene tests automatizados. Se valida con los scripts de [tools/e2e](../tools/e2e/README.md):
  un formulario que inicia un `DoDragDrop` real de `FileDrop` mientras `mouse_event` mueve el cursor
  (con y sin shake) y UI Automation para pulsar *Create ZIP*. Mueven el ratón real.
- Simular el botón pulsado requiere procesar la cola de mensajes del hilo origen (`DoEvents`) antes de
  `DoDragDrop`, o el drag termina al instante.
