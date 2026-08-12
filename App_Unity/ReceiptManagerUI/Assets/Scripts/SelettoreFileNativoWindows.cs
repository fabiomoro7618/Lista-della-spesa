#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine.EventSystems;

/// <summary>
/// Selettore di file nativo di Windows (la classica finestra "Apri file" di Esplora Risorse),
/// implementato tramite P/Invoke verso comdlg32.dll (GetOpenFileNameW). Sostituisce
/// EditorUtility.OpenFilePanel, disponibile solo in Editor, nelle build Standalone Windows,
/// dove l'assembly UnityEditor non esiste.
/// </summary>
internal static class SelettoreFileNativoWindows
{
    // Dichiarata come "class" (non struct): è il pattern richiesto affinché il marshaller
    // P/Invoke, nel campo "file", copi all'indietro nel buffer la stringa scritta dall'utente
    // dopo la chiusura della finestra di dialogo (marshaling bidirezionale [In, Out]).
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private class OpenFileName
    {
        public int structSize = Marshal.SizeOf(typeof(OpenFileName));
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public string filter;
        public string customFilter;
        public int maxCustFilter;
        public int filterIndex;
        public string file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defExt;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public string templateName;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt;
        public int flagsEx;
    }

    private const int OfnFileMustExist = 0x00001000;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnExplorer = 0x00080000;
    private const int OfnHideReadOnly = 0x00000004;

    // Impedisce che GetOpenFileName cambi la directory di lavoro corrente del processo:
    // un comportamento predefinito dell'API nativa che, senza questo flag, romperebbe i
    // percorsi relativi usati altrove nell'applicazione (es. Application.streamingAssetsPath).
    private const int OfnNoChangeDir = 0x00000008;

    // Buffer generoso per il percorso restituito: sufficiente per percorsi ben oltre il
    // classico limite MAX_PATH (260 caratteri) di Windows.
    private const int LunghezzaBufferPercorso = 4096;

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    /// <summary>
    /// Apre la finestra di dialogo "Apri file" nativa di Windows, filtrata sulle estensioni
    /// indicate, e restituisce il percorso completo del file selezionato, oppure null se
    /// l'utente ha annullato l'operazione.
    /// </summary>
    /// <param name="titolo">Titolo della finestra di dialogo.</param>
    /// <param name="descrizioneFiltro">Etichetta descrittiva del filtro (es. "Immagini scontrino").</param>
    /// <param name="estensioni">Estensioni ammesse, separate da virgola o punto e virgola, senza il punto iniziale (es. "png,jpg,jpeg").</param>
    public static string ApriFileDialog(string titolo, string descrizioneFiltro, string estensioni)
    {
        string pattern = string.Join(";", estensioni
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(estensione => "*." + estensione.Trim().TrimStart('*', '.')));

        // Formato filtro Win32: "Descrizione\0*.ext1;*.ext2\0" ripetuto per ogni voce, con
        // un ulteriore \0 finale a terminare l'intero blocco (doppio terminatore nullo).
        string filtro = $"{descrizioneFiltro} ({pattern})\0{pattern}\0Tutti i file (*.*)\0*.*\0\0";

        var ofn = new OpenFileName
        {
            dlgOwner = GetActiveWindow(),
            filter = filtro,
            file = new string('\0', LunghezzaBufferPercorso),
            maxFile = LunghezzaBufferPercorso,
            title = titolo,
            flags = OfnFileMustExist | OfnPathMustExist | OfnExplorer | OfnHideReadOnly | OfnNoChangeDir
        };

        // Deseleziona il pulsante Unity correntemente selezionato (quello che ha aperto questa
        // finestra) PRIMA di mostrare il dialog nativo: al ritorno del focus sulla finestra
        // Unity, un residuo del tasto Invio usato per confermare il file in Esplora Risorse può
        // altrimenti riattivare l'azione "Submit" dell'EventSystem su quel pulsante ancora
        // selezionato, dando l'impressione che la selezione del file scateni un'azione
        // automatica. Bug specifico della build Standalone: EditorUtility.OpenFilePanel non è
        // soggetto a questo comportamento perché non è una finestra nativa del sistema operativo.
        EventSystem.current?.SetSelectedGameObject(null);

        bool selezionato = GetOpenFileNameW(ofn);

        // Deseleziona di nuovo anche subito dopo la chiusura del dialog, per assorbire
        // eventuali eventi di tastiera residui prima che l'EventSystem li interpreti.
        EventSystem.current?.SetSelectedGameObject(null);

        if (!selezionato)
        {
            return null;
        }

        int indiceTerminatore = ofn.file.IndexOf('\0');
        return indiceTerminatore >= 0 ? ofn.file.Substring(0, indiceTerminatore) : ofn.file;
    }
}
#endif
