using System.Globalization;

namespace ZipDrop;

/// <summary>
/// All user-visible text, in English and Spanish (picked from the Windows display language).
/// Two languages don't justify .resx + satellite assemblies (D-021); if a third one arrives, migrate.
/// Override for testing with the environment variable ZIPDROP_LANG=en|es.
/// XAML uses {x:Static zd:Strings.Name}.
/// </summary>
public static class Strings
{
    public static readonly bool IsSpanish = DetectSpanish();

    /// <summary>Culture used for numbers ("1.4 GB" / "1,4 GB").</summary>
    public static CultureInfo Culture => IsSpanish ? CultureInfo.GetCultureInfo("es-ES") : CultureInfo.InvariantCulture;

    private static bool DetectSpanish()
    {
        var forced = Environment.GetEnvironmentVariable("ZIPDROP_LANG");
        if (!string.IsNullOrEmpty(forced)) return forced.StartsWith("es", StringComparison.OrdinalIgnoreCase);
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    }

    private static string T(string en, string es) => IsSpanish ? es : en;

    // ---------- Basket overlay ----------
    public static string DropFilesHere => T("Drop files here", "Suelta archivos aquí");
    public static string DropOrPaste => T("or paste with Ctrl+V · nothing is copied", "o pega con Ctrl+V · no se copia nada");
    public static string Items => T("Items", "Elementos");
    public static string ShowMissingTip => T("Show which items are missing", "Ver qué elementos faltan");
    public static string RemoveFromBasket => T("Remove from basket", "Quitar de la cesta");
    public static string RemoveMissingItems => T("Remove missing items", "Quitar los que faltan");
    public static string CreatingZip => T("Creating ZIP…", "Creando ZIP…");
    public static string Cancel => T("Cancel", "Cancelar");
    public static string OpenFolder => T("Open folder", "Abrir carpeta");
    public static string Copy => T("Copy", "Copiar");
    public static string Done => T("Done", "Listo");
    public static string DragToShare => T("Drag it into an email, chat or upload", "Arrástralo a un correo, chat o web");
    public static string DragToShareTip => T("Drag this ZIP anywhere — Gmail, WhatsApp Web, Teams, a folder…",
                                              "Arrastra este ZIP a donde quieras: Gmail, WhatsApp Web, Teams, una carpeta…");
    public static string CopyTip => T("Copy the ZIP file, then paste it in a chat, email or folder",
                                      "Copia el archivo ZIP y pégalo en un chat, correo o carpeta");
    public static string CouldNotCreate => T("Couldn't create the ZIP", "No se pudo crear el ZIP");
    public static string BackToBasket => T("Back to basket", "Volver a la cesta");
    public static string ClearTip => T("Empty the basket (files are not touched)", "Vaciar la cesta (tus archivos no se tocan)");
    public static string CreateZip => T("Create ZIP", "Crear ZIP");
    public static string DropToAdd => T("Drop to add", "Suelta para añadir");
    public static string AddToZip => T("Add to ZIP", "Añadir al ZIP");
    public static string AddTo(string items) => T($"Add to {items}", $"Añadir a {items}");
    public static string SettingsTip => T("Settings", "Ajustes");
    public static string HideTip => T("Hide (basket is kept)", "Ocultar (la cesta se conserva)");
    public static string Missing => T("missing", "falta");
    public static string Clear => T("Clear", "Vaciar");
    public static string ClickAgainToClear => T("Click again to clear", "Pulsa otra vez para vaciar");

    public static string ItemCount(int n) => IsSpanish
        ? (n == 1 ? "1 elemento" : $"{n} elementos")
        : (n == 1 ? "1 item" : $"{n} items");

    public static string MissingCount(int n) => T($"{n} missing", n == 1 ? "1 falta" : $"{n} faltan");
    public static string Added(int n) => T($"+{n} added", n == 1 ? "+1 añadido" : $"+{n} añadidos");
    public static string AlreadyInBasket(int n) => T($"{n} already in basket", $"{n} ya en la cesta");
    public static string NotFound(int n) => T($"{n} not found", n == 1 ? "1 no encontrado" : $"{n} no encontrados");
    public static string RemovedMissing(int n) => T($"Removed {n} missing", n == 1 ? "Quitado 1 que faltaba" : $"Quitados {n} que faltaban");
    public static string NothingToPaste => T("No files in the clipboard", "No hay archivos en el portapapeles");
    public static string CopiedZip => T("ZIP copied — paste it anywhere", "ZIP copiado: pégalo donde quieras");
    public static string ClipboardBusy => T("Clipboard is busy, try again", "El portapapeles está ocupado, inténtalo de nuevo");

    public static string Preparing => T("Preparing…", "Preparando…");
    public static string ProgressBytes(double fraction, string done, string total) =>
        T($"{fraction.ToString("P0", Culture)} · {done} of {total}", $"{fraction.ToString("P0", Culture)} · {done} de {total}");
    public static string ProgressFiles(int done, int total) => T($"{done} of {total} files", $"{done} de {total} archivos");
    public static string ZipCreated => T("ZIP created", "ZIP creado");
    public static string SkippedFiles(int n) => T($"{n} file(s) skipped (in use or no access)", $"{n} archivo(s) omitido(s) (en uso o sin acceso)");
    public static string MissingNotIncluded(int n) => T($"{n} missing item(s) not included", $"{n} elemento(s) que faltaban no incluido(s)");
    public static string Renamed(int n) => T($"{n} renamed to avoid name conflicts", $"{n} renombrado(s) para evitar nombres repetidos");
    public static string ZipCancelled => T("ZIP cancelled", "ZIP cancelado");
    public static string CouldNotOpenFolder => T("Could not open the folder", "No se pudo abrir la carpeta");
    public static string NothingToZip => T("Every item in the basket is missing.", "Todos los elementos de la cesta faltan.");
    public static string ZipReady(string name) => T($"{name} is ready.", $"{name} está listo.");
    public static string ZipFailed(string message) => T($"Could not create the ZIP: {message}", $"No se pudo crear el ZIP: {message}");
    public static string SaveZipTitle => T("Save ZIP", "Guardar ZIP");
    public static string ZipFilter => T("ZIP archive (*.zip)|*.zip", "Archivo ZIP (*.zip)|*.zip");
    public static string SuggestedName => "Archive.zip";

    // ---------- Tray / app ----------
    public static string TrayEmpty => T("ZipDrop — basket empty", "ZipDrop — cesta vacía");
    public static string TrayItems(string items, string size) => $"ZipDrop — {items} · {size}";
    public static string MenuOpen => T("Open ZipDrop", "Abrir ZipDrop");
    public static string MenuNewBasket => T("New basket", "Nueva cesta");
    public static string MenuSettings => T("Settings", "Ajustes");
    public static string MenuExit => T("Exit", "Salir");
    public static string ShortcutUnavailableTitle => T("Shortcut unavailable", "Atajo no disponible");
    public static string ChangeInSettings => T("Change it in Settings.", "Cámbialo en Ajustes.");
    public static string RunningTitle => T("ZipDrop is running", "ZipDrop está en marcha");
    public static string RunningText(string shortcut) =>
        T($"Press {shortcut} or shake while dragging files to open the basket.",
          $"Pulsa {shortcut} o agita el ratón mientras arrastras archivos para abrir la cesta.");
    public static string ErrorTitle => T("ZipDrop — error", "ZipDrop — error");
    public static string NewBasketConfirm(string items) =>
        T($"Start a new basket? The current one ({items}) will be emptied.\nYour files are not touched.",
          $"¿Empezar una cesta nueva? La actual ({items}) se vaciará.\nTus archivos no se tocan.");
    public static string ExitWhileZipping => T("A ZIP is being created. Cancel it and exit?", "Se está creando un ZIP. ¿Cancelarlo y salir?");
    public static string UnexpectedError(string message) =>
        T($"Something went wrong:\n{message}\n\nZipDrop keeps running.", $"Algo ha fallado:\n{message}\n\nZipDrop sigue funcionando.");

    // ---------- Settings ----------
    public static string SettingsWindowTitle => T("ZipDrop Settings", "Ajustes de ZipDrop");
    public static string SettingsHeader => T("Settings", "Ajustes");
    public static string GlobalShortcut => T("Global shortcut", "Atajo global");
    public static string GlobalShortcutHint => T("Shows or hides the basket from anywhere.", "Muestra u oculta la cesta desde cualquier sitio.");
    public static string ShortcutButtonTip => T("Click, then press the new key combination (Esc cancels)",
                                                "Haz clic y pulsa la nueva combinación (Esc cancela)");
    public static string PressKeys => T("Press keys…", "Pulsa teclas…");
    public static string ShakeToOpen => T("Shake to open", "Agitar para abrir");
    public static string ShakeHint => T("While dragging files from Explorer or the desktop, shake the mouse left-right.",
                                        "Mientras arrastras archivos desde el Explorador o el escritorio, agita el ratón a izquierda y derecha.");
    public static string ShakeSensitivity => T("Shake sensitivity", "Sensibilidad");
    public static string Low => T("Low", "Baja");
    public static string Medium => T("Medium", "Media");
    public static string High => T("High", "Alta");
    public static string ClearAfterZip => T("Clear basket after creating ZIP", "Vaciar la cesta al crear el ZIP");
    public static string ClearAfterZipHint => T("Kept if some files could not be read.", "Se conserva si algún archivo no se pudo leer.");
    public static string LaunchAtStartup => T("Launch at startup", "Iniciar con Windows");
    public static string LaunchAtStartupHint => T("Starts quietly in the system tray.", "Arranca en silencio en la bandeja del sistema.");
    public static string PrivacyNote => T("ZipDrop works entirely on this PC. No accounts, no cloud, no telemetry.",
                                          "ZipDrop funciona solo en este PC. Sin cuentas, sin nube, sin telemetría.");
    public static string Releases => T("Releases", "Versiones");
    public static string ShortcutNeedsModifier => T("Use Ctrl, Alt or Win plus a key.", "Usa Ctrl, Alt o Win más una tecla.");
    public static string ShortcutInvalid(string s) => T($"\"{s}\" is not a valid shortcut.", $"\"{s}\" no es un atajo válido.");
    public static string ShortcutTaken(string s) => T($"{s} is already used by another app.", $"{s} ya lo usa otra aplicación.");
    public static string ShakeUnavailable => T("Shake detection is unavailable (the mouse hook could not be installed).",
                                               "La detección del gesto no está disponible (no se pudo instalar el hook de ratón).");
}
