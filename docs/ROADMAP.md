# Roadmap

## Estado de las fases del MVP

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Proyecto y arquitectura · overlay · drag & drop · cesta · crear ZIP | ✅ Hecho |
| 2 | Bandeja del sistema · hotkey global · ajustes | ✅ Hecho |
| 3 | Gesto shake (investigación + implementación) | ✅ Hecho (ver D-009 y KNOWN_ISSUES) |
| 4 | Pulido: animaciones, errores, DPI, rendimiento, instalador | ✅ Hecho (instalador: script listo, compilarlo requiere Inno Setup 6) |

Verificado end-to-end en Windows 11 (25H2): drop OLE real, shake durante drag, hotkey, missing,
conflicto de nombres, *Create ZIP* con diálogo nativo, ajustes.

## Siguiente (pulido pendiente, pequeño)

- [ ] Probar a mano con Explorer real en monitores con DPI mixto (125 % + 100 %) y ajustar `PlaceNear`.
- [ ] Ajustar los valores de `ShakeOptions` con varios usuarios reales (ahora calibrados con trayectorias sintéticas).
- [ ] Texto de drop personalizado en Explorer ("Add to ZipDrop") vía formato `DropDescription`.
- [ ] Migrar a .NET 10 cuando el SDK esté disponible (cambiar `TargetFramework`).
- [ ] Firmar el ejecutable/instalador (reduce avisos de SmartScreen y falsos positivos de antivirus por el hook).
- [ ] Navegación por teclado completa en el overlay (Tab/Enter) y revisión de alto contraste.
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
