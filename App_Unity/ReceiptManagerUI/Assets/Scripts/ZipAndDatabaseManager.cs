using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

/// <summary>Esito dell'elaborazione di un singolo prodotto rispetto al database.</summary>
public enum EsitoProdotto
{
    Aggiunto,
    Aggiornato,
    Invariato
}

/// <summary>
/// Dettaglio dell'esito di un singolo prodotto, usato per popolare il pannello
/// "Dettaglio Prodotti Elaborati" nell'interfaccia utente.
/// </summary>
public struct DettaglioProdottoElaborato
{
    public string Nome;
    public float Prezzo;

    /// <summary>Prezzo precedente alla modifica: valorizzato solo quando <see cref="Esito"/> è Aggiornato.</summary>
    public float PrezzoPrecedente;
    public int Quantita;
    public EsitoProdotto Esito;
}

/// <summary>
/// Risultato di un'operazione di aggiornamento del database: contiene i contatori
/// e il dettaglio per-prodotto da mostrare nell'interfaccia utente.
/// </summary>
public struct RisultatoAggiornamentoDatabase
{
    public int ProdottiAggiunti;
    public int ProdottiAggiornati;
    public int ProdottiInvariati;
    public List<DettaglioProdottoElaborato> Dettagli;
}

/// <summary>
/// Gestisce il ciclo di vita del backup SQLite compresso in formato ZIP:
/// estrazione in una cartella temporanea, apertura/scrittura del database e
/// ricompattazione finale del file ZIP.
///
/// L'accesso al database avviene tramite <see cref="SqliteLiteHelper"/>, un wrapper
/// leggero basato su P/Invoke verso la libreria nativa SQLite3: non dipende da
/// "Mono.Data.Sqlite" (rimosso nei profili .NET Standard 2.1 / IL2CPP di Unity 6) né
/// da pacchetti NuGet/UPM aggiuntivi. Vedere i commenti in SqliteLiteHelper.cs per i
/// requisiti della libreria nativa sulle diverse piattaforme.
/// </summary>
public class ZipAndDatabaseManager : MonoBehaviour
{
    // Nome fisso del file database SQLite contenuto nello ZIP: non configurabile né in
    // Inspector né da UI, per restare sempre coerente con il formato di backup atteso
    // dall'app principale "Lista della spesa".
    private const string NomeFileDatabase = "ekz_db";

    [Header("Configurazione file di backup")]
    [Tooltip("Nome del file ZIP di backup, situato in Application.persistentDataPath.")]
    [SerializeField]
    private string nomeFileZip = "backup_database.zip";

    [Header("Configurazione tabella")]
    [Tooltip("Nome della tabella in cui inserire/aggiornare i prodotti.")]
    [SerializeField]
    private string nomeTabella = "myitems";

    // Schema reale rilevato tramite "PRAGMA table_info(myitems);" sul database di produzione
    // (app "Lista della spesa"): CREATE TABLE myitems(id INTEGER PRIMARY KEY, name TEXT,
    // last_cat TEXT, counttype TEXT, price FLOAT, barcode TEXT, buycount INTEGER).
    // Non esistono colonne "nome"/"prezzo"/"quantita"/"data_aggiornamento": i nomi corretti
    // sono esposti come campi configurabili così da poter essere adattati senza modificare
    // il codice se lo schema dovesse cambiare in futuro.
    [Header("Mappatura colonne (schema reale del database esistente)")]
    [Tooltip("Colonna che contiene il nome del prodotto.")]
    [SerializeField]
    private string colonnaNome = "name";

    [Tooltip("Colonna che contiene il prezzo del prodotto.")]
    [SerializeField]
    private string colonnaPrezzo = "price";

    [Tooltip("Colonna che contiene il contatore/quantità d'acquisto del prodotto.")]
    [SerializeField]
    private string colonnaQuantita = "buycount";

    /// <summary>
    /// Percorso ZIP scelto esplicitamente dall'utente (es. tramite un selettore di file),
    /// che ha priorità sul percorso predefinito in Application.persistentDataPath.
    /// </summary>
    private string percorsoZipPersonalizzato;

    /// <summary>
    /// Percorso completo del file ZIP di backup persistente (predefinito o personalizzato).
    /// Proprietà calcolata "pura": si limita a comporre una stringa (Path.Combine) e non ha
    /// alcun effetto collaterale, non solleva eventi né avvia alcuna elaborazione. Il fallback
    /// al percorso predefinito avviene quindi in modo del tutto silenzioso ogni volta che
    /// questa proprietà (o <see cref="PercorsoZipCorrente"/>) viene letta, finché l'utente non
    /// sceglie esplicitamente un backup diverso tramite <see cref="ImpostaPercorsoZipPersonalizzato"/>.
    /// </summary>
    private string PercorsoZip => string.IsNullOrEmpty(percorsoZipPersonalizzato)
        ? Path.Combine(Application.persistentDataPath, nomeFileZip)
        : percorsoZipPersonalizzato;

    /// <summary>
    /// Cartella temporanea dedicata in cui viene estratto l'intero contenuto dello ZIP di
    /// backup (database, "settings.ekz" ed eventuali altri file), non solo il database.
    /// Alcuni backup dell'app principale richiedono questi file aggiuntivi accanto al
    /// database per essere considerati validi: vanno quindi preservati e reinclusi.
    /// </summary>
    private string PercorsoCartellaEstrazione => Path.Combine(Application.temporaryCachePath, "BackupEstratto");

    /// <summary>Percorso completo del database SQLite una volta estratto in cache temporanea.</summary>
    private string PercorsoDatabaseEstratto => Path.Combine(PercorsoCartellaEstrazione, NomeFileDatabase);

    /// <summary>Percorso ZIP attualmente in uso (utile per mostrarlo nell'interfaccia utente).</summary>
    public string PercorsoZipCorrente => PercorsoZip;

    /// <summary>
    /// Imposta un percorso ZIP personalizzato (es. selezionato dall'utente tramite un file
    /// picker), da usare al posto del percorso predefinito in Application.persistentDataPath.
    /// </summary>
    /// <param name="percorsoCompleto">Percorso completo del file ZIP di backup da utilizzare.</param>
    public void ImpostaPercorsoZipPersonalizzato(string percorsoCompleto)
    {
        percorsoZipPersonalizzato = percorsoCompleto;
    }

    /// <summary>
    /// Rimuove il percorso ZIP personalizzato eventualmente impostato: <see cref="PercorsoZip"/>
    /// torna a risolversi sul percorso predefinito in Application.persistentDataPath.
    /// </summary>
    public void RimuoviPercorsoZipPersonalizzato()
    {
        percorsoZipPersonalizzato = null;
    }

    /// <summary>
    /// Estrae TUTTO il contenuto del backup ZIP (database, "settings.ekz" ed eventuali altri
    /// file presenti nell'archivio originale) in una cartella temporanea dedicata
    /// (<see cref="PercorsoCartellaEstrazione"/>), ripulita da eventuali estrazioni precedenti.
    /// Solo il database viene poi aperto/modificato, ma tutti gli altri file vengono preservati
    /// per essere reinclusi tali e quali in fase di ricompattazione.
    /// </summary>
    /// <returns>Il percorso completo del database estratto, pronto per essere aperto.</returns>
    public string EstraiDatabaseDaZip()
    {
        if (!File.Exists(PercorsoZip))
        {
            throw new FileNotFoundException($"File di backup ZIP non trovato: {PercorsoZip}");
        }

        // Ripulisce la cartella di estrazione da un'eventuale estrazione precedente, così da
        // non lasciare file obsoleti non presenti nello ZIP corrente (es. se si cambia backup).
        if (Directory.Exists(PercorsoCartellaEstrazione))
        {
            Directory.Delete(PercorsoCartellaEstrazione, recursive: true);
        }
        Directory.CreateDirectory(PercorsoCartellaEstrazione);

        using (ZipArchive archivio = ZipFile.OpenRead(PercorsoZip))
        {
            foreach (ZipArchiveEntry voce in archivio.Entries)
            {
                // Le voci che rappresentano cartelle hanno nome vuoto (il percorso termina con "/").
                if (string.IsNullOrEmpty(voce.Name))
                {
                    continue;
                }

                string percorsoDestinazione = Path.Combine(PercorsoCartellaEstrazione, voce.FullName);

                string cartellaDestinazione = Path.GetDirectoryName(percorsoDestinazione);
                if (!string.IsNullOrEmpty(cartellaDestinazione) && !Directory.Exists(cartellaDestinazione))
                {
                    Directory.CreateDirectory(cartellaDestinazione);
                }

                voce.ExtractToFile(percorsoDestinazione, overwrite: true);
            }
        }

        if (!File.Exists(PercorsoDatabaseEstratto))
        {
            throw new FileNotFoundException(
                $"Il file '{NomeFileDatabase}' non è presente all'interno dello ZIP '{PercorsoZip}'.");
        }

        return PercorsoDatabaseEstratto;
    }

    /// <summary>
    /// Ricompatta in un nuovo file ZIP di backup TUTTI i file presenti nella cartella di
    /// estrazione (database aggiornato, "settings.ekz" ed eventuali altri file originariamente
    /// presenti nell'archivio), non solo il database: alcuni backup dell'app principale
    /// richiedono questi file aggiuntivi accanto al database per essere considerati validi.
    ///
    /// Va chiamato solo dopo che ogni connessione al database (<see cref="SqliteLiteHelper"/>)
    /// è stata chiusa: <see cref="AggiornaProdotti"/> apre e dispone la propria connessione
    /// internamente tramite un blocco "using" prima di restituire il controllo al chiamante,
    /// quindi al momento in cui questo metodo viene invocato il file del database è già
    /// completamente libero da handle aperti e può essere letto/compresso senza conflitti.
    /// </summary>
    public void RicomprimiDatabaseInZip()
    {
        if (!File.Exists(PercorsoDatabaseEstratto))
        {
            throw new FileNotFoundException(
                $"Nessun database estratto da ricomprimere: {PercorsoDatabaseEstratto}");
        }

        // Un nuovo ZIP viene creato da zero per garantire che non restino voci obsolete.
        if (File.Exists(PercorsoZip))
        {
            File.Delete(PercorsoZip);
        }

        using (ZipArchive archivio = ZipFile.Open(PercorsoZip, ZipArchiveMode.Create))
        {
            foreach (string percorsoFile in Directory.GetFiles(PercorsoCartellaEstrazione, "*", SearchOption.AllDirectories))
            {
                // Nome della voce nello ZIP: percorso relativo alla cartella di estrazione,
                // con separatori "/" (convenzione standard del formato ZIP).
                string nomeVoce = Path.GetRelativePath(PercorsoCartellaEstrazione, percorsoFile).Replace('\\', '/');
                archivio.CreateEntryFromFile(percorsoFile, nomeVoce);
            }
        }
    }

    /// <summary>
    /// Inserisce o aggiorna, all'interno della tabella "myitems", l'elenco dei prodotti
    /// riconosciuti nello scontrino. Un prodotto viene considerato "invariato" se esiste
    /// già con lo stesso prezzo, "aggiornato" se esiste con prezzo differente e "aggiunto"
    /// se non è ancora presente.
    /// </summary>
    /// <param name="prodotti">Elenco dei prodotti restituiti dall'analisi dello scontrino.</param>
    /// <returns>I contatori delle operazioni eseguite sul database.</returns>
    public RisultatoAggiornamentoDatabase AggiornaProdotti(List<ProdottoScontrino> prodotti)
    {
        var risultato = new RisultatoAggiornamentoDatabase
        {
            Dettagli = new List<DettaglioProdottoElaborato>()
        };

        if (prodotti == null || prodotti.Count == 0)
        {
            return risultato;
        }

        using (var database = new SqliteLiteHelper(PercorsoDatabaseEstratto))
        {
            AssicuraTabellaEsistente(database);

            database.IniziaTransazione();
            try
            {
                foreach (ProdottoScontrino prodotto in prodotti)
                {
                    if (string.IsNullOrWhiteSpace(prodotto.nome))
                    {
                        continue;
                    }

                    float? prezzoEsistente = database.LeggiPrimoValoreFloat(
                        $"SELECT {colonnaPrezzo} FROM {nomeTabella} WHERE {colonnaNome} = ? COLLATE NOCASE LIMIT 1;",
                        prodotto.nome);

                    if (prezzoEsistente == null)
                    {
                        InserisciProdotto(database, prodotto);
                        risultato.ProdottiAggiunti++;
                        risultato.Dettagli.Add(new DettaglioProdottoElaborato
                        {
                            Nome = prodotto.nome,
                            Prezzo = prodotto.prezzo,
                            Quantita = prodotto.quantita,
                            Esito = EsitoProdotto.Aggiunto
                        });
                    }
                    else if (!PrezziEquivalenti(prezzoEsistente.Value, prodotto.prezzo))
                    {
                        AggiornaProdottoEsistente(database, prodotto);
                        risultato.ProdottiAggiornati++;
                        risultato.Dettagli.Add(new DettaglioProdottoElaborato
                        {
                            Nome = prodotto.nome,
                            Prezzo = prodotto.prezzo,
                            PrezzoPrecedente = prezzoEsistente.Value,
                            Quantita = prodotto.quantita,
                            Esito = EsitoProdotto.Aggiornato
                        });
                    }
                    else
                    {
                        risultato.ProdottiInvariati++;
                        risultato.Dettagli.Add(new DettaglioProdottoElaborato
                        {
                            Nome = prodotto.nome,
                            Prezzo = prodotto.prezzo,
                            Quantita = prodotto.quantita,
                            Esito = EsitoProdotto.Invariato
                        });
                    }
                }

                database.Commit();
            }
            catch
            {
                // In caso di errore durante l'aggiornamento, annulla tutte le modifiche
                // effettuate finora nella transazione per non lasciare il database in uno
                // stato parzialmente aggiornato.
                database.Rollback();
                throw;
            }
        }

        return risultato;
    }

    /// <summary>
    /// Crea la tabella "myitems" se non esiste ancora nel database, riproducendo lo schema
    /// reale del database di produzione (rilevato con "PRAGMA table_info(myitems);"), così
    /// da rendere lo script utilizzabile anche su un database vuoto o appena creato senza
    /// introdurre colonne non previste dall'app principale.
    /// </summary>
    private void AssicuraTabellaEsistente(SqliteLiteHelper database)
    {
        database.EseguiNonQuery(
            $@"CREATE TABLE IF NOT EXISTS {nomeTabella} (
                id INTEGER PRIMARY KEY,
                {colonnaNome} TEXT,
                last_cat TEXT,
                counttype TEXT,
                {colonnaPrezzo} FLOAT,
                barcode TEXT,
                {colonnaQuantita} INTEGER
            );");
    }

    /// <summary>Inserisce un nuovo prodotto nella tabella.</summary>
    private void InserisciProdotto(SqliteLiteHelper database, ProdottoScontrino prodotto)
    {
        // "last_cat", "counttype" e "barcode" non sono desumibili dalla scansione dello
        // scontrino. Il database esistente valorizza questi campi con la stringa letterale
        // "null" (non un vero NULL SQL) per i prodotti privi di informazione: manteniamo
        // la stessa convenzione per restare coerenti con quanto l'app principale si aspetta.
        database.EseguiNonQuery(
            $@"INSERT INTO {nomeTabella} ({colonnaNome}, last_cat, counttype, {colonnaPrezzo}, barcode, {colonnaQuantita})
               VALUES (?, 'null', 'null', ?, 'null', ?);",
            prodotto.nome, prodotto.prezzo, prodotto.quantita);
    }

    /// <summary>Aggiorna prezzo e quantità di un prodotto già presente, senza toccare gli altri campi.</summary>
    private void AggiornaProdottoEsistente(SqliteLiteHelper database, ProdottoScontrino prodotto)
    {
        database.EseguiNonQuery(
            $@"UPDATE {nomeTabella}
               SET {colonnaPrezzo} = ?, {colonnaQuantita} = ?
               WHERE {colonnaNome} = ? COLLATE NOCASE;",
            prodotto.prezzo, prodotto.quantita, prodotto.nome);
    }

    /// <summary>
    /// Confronta due prezzi tollerando piccoli errori di arrotondamento tipici dei valori "float".
    /// </summary>
    private bool PrezziEquivalenti(float prezzoA, float prezzoB)
    {
        const float tolleranza = 0.001f;
        return Mathf.Abs(prezzoA - prezzoB) < tolleranza;
    }
}
