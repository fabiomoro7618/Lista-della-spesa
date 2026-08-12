using System;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Wrapper leggero e autonomo attorno alla libreria nativa SQLite3 (P/Invoke diretto sulle
/// funzioni della C API), pensato come sostituto di "Mono.Data.Sqlite" — namespace rimosso
/// nei profili .NET Standard 2.1 / IL2CPP usati da Unity 6 e quindi non più disponibile.
///
/// Non richiede alcun pacchetto NuGet/UPM aggiuntivo: è sufficiente che la libreria nativa
/// "sqlite3" sia risolvibile a runtime dalla piattaforma di destinazione:
///  - Windows (Standalone): scaricare "sqlite3.dll" (x64, di pubblico dominio da
///    https://www.sqlite.org/download.html) e collocarlo in "Assets/Plugins/x86_64/".
///  - macOS: "libsqlite3.dylib" è normalmente già presente come libreria di sistema.
///  - Linux: "libsqlite3.so" è normalmente già presente come libreria di sistema.
///  - Android / iOS: la libreria è generalmente già inclusa nel sistema operativo.
/// </summary>
public sealed class SqliteLiteHelper : IDisposable
{
    // Codici di ritorno rilevanti della C API di SQLite3.
    private const int SQLITE_OK = 0;
    private const int SQLITE_ROW = 100;
    private const int SQLITE_DONE = 101;
    private const int SQLITE_NULL = 5;

    // Puntatore nativo -1: indica a SQLite di copiare immediatamente il testo passato
    // (SQLITE_TRANSIENT), necessario perché il buffer gestito può essere spostato/liberato dal GC.
    private static readonly IntPtr SQLITE_TRANSIENT = new IntPtr(-1);

    private IntPtr connessioneNativa;

    /// <summary>Apre (o crea, se non esiste) il file di database SQLite indicato.</summary>
    /// <param name="percorsoDatabase">Percorso completo del file .db da aprire.</param>
    public SqliteLiteHelper(string percorsoDatabase)
    {
        int esito = sqlite3_open(CodificaUtf8ConTerminatore(percorsoDatabase), out connessioneNativa);
        if (esito != SQLITE_OK)
        {
            string messaggio = LeggiUltimoErrore();
            connessioneNativa = IntPtr.Zero;
            throw new InvalidOperationException($"Impossibile aprire il database SQLite '{percorsoDatabase}': {messaggio}");
        }
    }

    /// <summary>Esegue una query senza risultati (CREATE TABLE, INSERT, UPDATE, DELETE, ...).</summary>
    /// <param name="sql">Testo SQL, con eventuali segnaposto posizionali "?".</param>
    /// <param name="parametri">Valori da associare ai segnaposto, nello stesso ordine.</param>
    public void EseguiNonQuery(string sql, params object[] parametri)
    {
        IntPtr istruzione = Prepara(sql);
        try
        {
            AssociaParametri(istruzione, parametri);

            int esito = sqlite3_step(istruzione);
            if (esito != SQLITE_DONE && esito != SQLITE_ROW)
            {
                throw new InvalidOperationException($"Errore durante l'esecuzione della query SQLite: {LeggiUltimoErrore()}");
            }
        }
        finally
        {
            sqlite3_finalize(istruzione);
        }
    }

    /// <summary>
    /// Esegue una query e restituisce il primo valore numerico della prima riga come "float",
    /// oppure null se la query non restituisce alcuna riga o il valore è NULL.
    /// </summary>
    /// <param name="sql">Testo SQL, con eventuali segnaposto posizionali "?".</param>
    /// <param name="parametri">Valori da associare ai segnaposto, nello stesso ordine.</param>
    public float? LeggiPrimoValoreFloat(string sql, params object[] parametri)
    {
        IntPtr istruzione = Prepara(sql);
        try
        {
            AssociaParametri(istruzione, parametri);

            int esito = sqlite3_step(istruzione);
            if (esito != SQLITE_ROW)
            {
                return null;
            }

            if (sqlite3_column_type(istruzione, 0) == SQLITE_NULL)
            {
                return null;
            }

            return (float)sqlite3_column_double(istruzione, 0);
        }
        finally
        {
            sqlite3_finalize(istruzione);
        }
    }

    /// <summary>Avvia una transazione esplicita.</summary>
    public void IniziaTransazione() => EseguiNonQuery("BEGIN TRANSACTION;");

    /// <summary>Conferma (commit) la transazione corrente.</summary>
    public void Commit() => EseguiNonQuery("COMMIT;");

    /// <summary>Annulla (rollback) la transazione corrente.</summary>
    public void Rollback() => EseguiNonQuery("ROLLBACK;");

    /// <summary>Chiude la connessione nativa al database, liberando le risorse non gestite.</summary>
    public void Dispose()
    {
        if (connessioneNativa != IntPtr.Zero)
        {
            sqlite3_close(connessioneNativa);
            connessioneNativa = IntPtr.Zero;
        }
    }

    /// <summary>Compila (prepara) una istruzione SQL, restituendo il relativo statement nativo.</summary>
    private IntPtr Prepara(string sql)
    {
        byte[] sqlUtf8 = CodificaUtf8ConTerminatore(sql);
        int esito = sqlite3_prepare_v2(connessioneNativa, sqlUtf8, -1, out IntPtr istruzione, IntPtr.Zero);
        if (esito != SQLITE_OK)
        {
            throw new InvalidOperationException($"Errore nella preparazione della query SQLite: {LeggiUltimoErrore()}");
        }
        return istruzione;
    }

    /// <summary>
    /// Associa i parametri posizionali (indicizzati da 1, come richiesto da SQLite) allo statement.
    /// Tipi supportati: string, int, float, double e null.
    /// </summary>
    private void AssociaParametri(IntPtr istruzione, object[] parametri)
    {
        for (int i = 0; i < parametri.Length; i++)
        {
            int indice = i + 1;
            object valore = parametri[i];

            switch (valore)
            {
                case null:
                    sqlite3_bind_null(istruzione, indice);
                    break;
                case string testo:
                    sqlite3_bind_text(istruzione, indice, CodificaUtf8ConTerminatore(testo), -1, SQLITE_TRANSIENT);
                    break;
                case float numeroFloat:
                    sqlite3_bind_double(istruzione, indice, numeroFloat);
                    break;
                case double numeroDouble:
                    sqlite3_bind_double(istruzione, indice, numeroDouble);
                    break;
                case int numeroInt:
                    sqlite3_bind_int(istruzione, indice, numeroInt);
                    break;
                default:
                    throw new NotSupportedException($"Tipo di parametro non supportato per SQLite: {valore.GetType()}");
            }
        }
    }

    /// <summary>Legge l'ultimo messaggio di errore riportato dalla connessione nativa.</summary>
    private string LeggiUltimoErrore()
    {
        IntPtr puntatoreErrore = sqlite3_errmsg(connessioneNativa);
        return puntatoreErrore == IntPtr.Zero ? "errore sconosciuto" : Marshal.PtrToStringUTF8(puntatoreErrore);
    }

    /// <summary>Converte una stringa gestita in un array di byte UTF-8 terminato da NUL, come richiesto dalla C API.</summary>
    private static byte[] CodificaUtf8ConTerminatore(string testo)
    {
        byte[] contenuto = Encoding.UTF8.GetBytes(testo ?? string.Empty);
        byte[] conTerminatore = new byte[contenuto.Length + 1];
        Buffer.BlockCopy(contenuto, 0, conTerminatore, 0, contenuto.Length);
        return conTerminatore; // l'ultimo byte resta a 0, fungendo da terminatore NUL
    }

    // ---------------------------------------------------------------------
    // Dichiarazioni P/Invoke della C API di SQLite3 (solo le funzioni necessarie).
    // ---------------------------------------------------------------------

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open(byte[] percorsoUtf8, out IntPtr db);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sqlUtf8, int numeroByte, out IntPtr istruzione, IntPtr codaNonUtilizzata);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr istruzione);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr istruzione);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr istruzione, int indice, byte[] valoreUtf8, int numeroByte, IntPtr destructor);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_double(IntPtr istruzione, int indice, double valore);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int(IntPtr istruzione, int indice, int valore);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_null(IntPtr istruzione, int indice);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr istruzione, int colonna);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern double sqlite3_column_double(IntPtr istruzione, int colonna);

    [DllImport("sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);
}
