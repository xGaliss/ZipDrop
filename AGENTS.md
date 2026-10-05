# Notas para agentes

Antes de cambiar nada, lee `docs/ARCHITECTURE.md` y `docs/DECISIONS.md`. Al terminar, actualiza los
documentos afectados (decisión nueva → `DECISIONS.md`; limitación → `KNOWN_ISSUES.md`; estado → `ROADMAP.md`).

- Producto: micro-utilidad. Si una función no mejora "recopilar → crear ZIP", no entra (ver ROADMAP → Futuro).
- Lógica testeable en `src/ZipDrop.Core` (sin WPF). UI/Win32 en `src/ZipDrop`. P/Invoke solo en `Interop/NativeMethods.cs`.
- `Basket` solo se toca desde el hilo de UI. El callback del hook de ratón debe seguir siendo trivial.
- Nunca devolver `DragDropEffects.Move` en un drop. Nunca sobrescribir entradas del ZIP.
- Parámetros del shake: solo en `ShakeOptions`. Cambios en el algoritmo → añadir trayectorias en `ShakeDetectorTests`.
- Comentarios de código y textos de UI en inglés; documentación en español.
- Comprobar: `dotnet build` sin warnings, `dotnet test` en verde. E2E opcional: `tools/e2e`.
- Sin telemetría, red ni servicios externos. Nunca.
