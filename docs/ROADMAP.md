# Roadmap

## Estado de las fases del MVP

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Proyecto y arquitectura · overlay · drag & drop · cesta · crear ZIP | ✅ Hecho |
| 2 | Bandeja del sistema · hotkey global · ajustes | ✅ Hecho |
| 3 | Gesto shake (investigación + implementación) | ✅ Hecho (ver D-009 y KNOWN_ISSUES) |
| 4 | Pulido: animaciones, errores, DPI, rendimiento, instalador | ✅ Hecho (instalador: script listo, compilarlo requiere Inno Setup 6) |

Verificado end-to-end en Windows 11: drop OLE real, **shake con el Explorador real**, hotkey, missing,
conflicto de nombres, *Create ZIP* con diálogo nativo, ajustes.

## Lanzamiento v0.1.0

- [x] Pulido de UI: iconos reales, pila de vista previa, zona de drop, cabecera con logo.
- [x] Licencia MIT, README en inglés con GIF, CONTRIBUTING, plantillas de issues.
- [x] CI (build + tests) y release automática por tag (instalador, portable, framework-dependent).
- [x] Repo público, topics y release `v0.1.0` (instalador, portable, framework-dependent, SHA256SUMS).
- [ ] Subir `docs/media/social-preview.png` como *social preview* (Settings → General; no hay API).
- [ ] Publicar en comunidades (borradores locales en `promo/`, fuera del repo).

## v0.2.0

- [x] Arrastrar el ZIP terminado fuera de la cesta (Gmail, WhatsApp Web, Teams, carpetas…) — D-022.
- [x] Botón **Copy** del ZIP al portapapeles y `Ctrl+V` para añadir archivos copiados — D-022.
- [x] Atajo por defecto `Ctrl+Alt+Z` en instalaciones nuevas, comprobando la distribución de teclado — D-023.
- [x] Interfaz en español según el idioma de Windows — D-021.
- [x] Versión y enlaces en Ajustes.

## Siguiente (pulido pendiente, pequeño)

- [ ] Probar a mano con Explorer real en monitores con DPI mixto (125 % + 100 %) y ajustar `PlaceNear`.
- [ ] Ajustar los valores de `ShakeOptions` con varios usuarios reales (ahora calibrados con trayectorias sintéticas).
- [ ] Texto de drop personalizado en Explorer ("Add to ZipDrop") vía formato `DropDescription`.
- [ ] Migrar a .NET 10 cuando el SDK esté disponible (cambiar `TargetFramework`).
- [ ] Firmar el ejecutable/instalador (reduce avisos de SmartScreen y falsos positivos de antivirus por el hook).
- [ ] Navegación por teclado completa en el overlay (Enter = Create ZIP, Supr = quitar) y revisión de alto contraste.
- [ ] Miniatura de arrastre al sacar el ZIP (`IDragSourceHelper`).
- [ ] Nombre sugerido más listo (`proyecto.zip` si es una sola carpeta; fecha en el resto).
- [ ] Guía de primer arranque (3 s) con el gesto y el atajo.
- [ ] Recordar la cesta entre reinicios.
- [ ] Publicar en winget y Microsoft Store.
- [ ] Tests de `HotkeyGesture` (hoy en el proyecto WPF; moverlo a un proyecto testeable si crece).

## Futuro — NO implementar todavía

El código está preparado para no bloquear estas ideas (ver ARCHITECTURE → Puntos de extensión).

- **Varias cestas** (`Work.zip — 12 · Photos.zip — 31 · Client.zip — 6`).
- **ZIP existente como cesta**: soltar `project.zip` y seguir añadiendo.
- **ZIP con contraseña.**
- **7z.**
- **Niveles de compresión** elegibles (hoy: política automática, D-007).
- **Drag actions**: DROP → ZIP · Folder · Upload · Share · Script.
- Persistir la cesta entre reinicios (hoy es temporal por diseño).
- Soporte de archivos virtuales (adjuntos de Outlook, contenido de ZIPs abiertos en Explorer): formato
  `FileGroupDescriptorW`/`FileContents`.
