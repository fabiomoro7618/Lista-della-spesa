using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Controller dell'interfaccia utente per il flusso di elaborazione scontrini:
/// coordina la selezione del file immagine e del backup ZIP, la chiamata ad
/// <see cref="AnthropicService"/> e l'aggiornamento del database tramite
/// <see cref="ZipAndDatabaseManager"/>, mostrando a schermo lo stato dell'operazione
/// (log e contatori Aggiunti/Aggiornati/Invariati).
/// </summary>
public class ReceiptUIController : MonoBehaviour
{
    [Header("Servizi")]
    [SerializeField] private AnthropicService servizioAnthropic;
    [SerializeField] private ZipAndDatabaseManager gestoreDatabase;

    [Header("Pulsanti")]
    [SerializeField] private Button pulsanteSelezionaScontrino;
    [SerializeField] private Button pulsanteSelezionaBackupZip;
    [SerializeField] private Button pulsanteElaboraConAI;
    [SerializeField] private Button pulsanteSalvaModificheZip;
    [SerializeField] private Button pulsanteReset;

    [Header("Configurazione Chiave API")]
    [Tooltip("Pannello mostrato solo quando manca una chiave API Anthropic valida, per permettere " +
             "all'utente di inserirla e salvarla al volo direttamente dall'eseguibile.")]
    [SerializeField] private GameObject pannelloApiKey;
    [SerializeField] private TMP_InputField campoApiKey;
    [SerializeField] private Button pulsanteSalvaApiKey;

    [Header("Contatori")]
    [SerializeField] private TMP_Text testoContatoreAggiunti;
    [SerializeField] private TMP_Text testoContatoreAggiornati;
    [SerializeField] private TMP_Text testoContatoreInvariati;

    [Header("Liste prodotti (scrollview separate)")]
    [SerializeField] private TMP_Text testoListaAggiunti;
    [SerializeField] private TMP_Text testoListaAggiornati;
    [SerializeField] private TMP_Text testoListaInvariati;

    [Header("Log ed elementi accessori")]
    [SerializeField] private TMP_Text testoLog;
    [SerializeField] private TMP_Text testoPercorsoBackupZip;
    [SerializeField] private RawImage anteprimaScontrino;
    [SerializeField] private GameObject indicatoreCaricamento;

    [Header("Protezione click-through (build Standalone)")]
    [Tooltip("CanvasGroup sulla Canvas principale, usato per bloccare temporaneamente i raycast " +
             "sulla UI subito dopo la chiusura del file picker nativo di Windows: previene il " +
             "click 'passante' che, chiudendo il dialog, può altrimenti attivare per errore il " +
             "pulsante Unity sottostante alle coordinate del cursore.")]
    [SerializeField] private CanvasGroup canvasGroupPrincipale;

    // Testo mostrato per il percorso di backup ZIP quando non ne è selezionato uno
    // personalizzato: stesso testo con cui UISceneBuilder inizializza il campo alla creazione
    // della UI, riusato qui per riportare "Reset" allo stato realmente iniziale/vergine.
    private const string TestoBackupPredefinito = "Backup: predefinito (Application.persistentDataPath)";

    private Texture2D texturePendente;
    private int contatoreAggiunti;
    private int contatoreAggiornati;
    private int contatoreInvariati;
    private bool elaborazioneInCorso;

    private void Awake()
    {
        if (pulsanteSelezionaScontrino != null)
        {
            pulsanteSelezionaScontrino.onClick.AddListener(OnClickSelezionaScontrino);
        }

        if (pulsanteSelezionaBackupZip != null)
        {
            pulsanteSelezionaBackupZip.onClick.AddListener(OnClickSelezionaBackupZip);
        }

        if (pulsanteElaboraConAI != null)
        {
            pulsanteElaboraConAI.onClick.AddListener(OnClickElaboraConAI);
        }

        if (pulsanteSalvaModificheZip != null)
        {
            pulsanteSalvaModificheZip.onClick.AddListener(OnClickSalvaModificheZip);
        }

        if (pulsanteReset != null)
        {
            pulsanteReset.onClick.AddListener(OnClickReset);
        }

        if (pulsanteSalvaApiKey != null)
        {
            pulsanteSalvaApiKey.onClick.AddListener(OnClickSalvaApiKey);
        }

        CaricaConfigurazionePersistente();

        AggiornaContatoriUI();
        ImpostaStatoCaricamento(false);
    }

    private void OnDestroy()
    {
        if (pulsanteSelezionaScontrino != null)
        {
            pulsanteSelezionaScontrino.onClick.RemoveListener(OnClickSelezionaScontrino);
        }

        if (pulsanteSelezionaBackupZip != null)
        {
            pulsanteSelezionaBackupZip.onClick.RemoveListener(OnClickSelezionaBackupZip);
        }

        if (pulsanteElaboraConAI != null)
        {
            pulsanteElaboraConAI.onClick.RemoveListener(OnClickElaboraConAI);
        }

        if (pulsanteSalvaModificheZip != null)
        {
            pulsanteSalvaModificheZip.onClick.RemoveListener(OnClickSalvaModificheZip);
        }

        if (pulsanteReset != null)
        {
            pulsanteReset.onClick.RemoveListener(OnClickReset);
        }

        if (pulsanteSalvaApiKey != null)
        {
            pulsanteSalvaApiKey.onClick.RemoveListener(OnClickSalvaApiKey);
        }
    }

    /// <summary>
    /// Gestisce il click sul pulsante di selezione dello scontrino: apre un selettore di file
    /// e carica l'immagine in anteprima, senza avviare ancora l'elaborazione (che parte solo
    /// con il pulsante "Elabora Scontrino con AI").
    /// </summary>
    private void OnClickSelezionaScontrino()
    {
        if (elaborazioneInCorso)
        {
            AggiungiLog("Elaborazione già in corso: attendere il completamento.");
            return;
        }

        string percorsoImmagine = ApriSelettoreFile("Seleziona immagine scontrino", "Immagini scontrino", "png,jpg,jpeg");
        if (string.IsNullOrEmpty(percorsoImmagine))
        {
            // L'utente ha annullato la selezione: nessuna azione da eseguire.
            return;
        }

        Texture2D immagineCaricata = CaricaTextureDaFile(percorsoImmagine);
        if (immagineCaricata == null)
        {
            AggiungiLog($"Impossibile caricare il file selezionato: {percorsoImmagine}");
            return;
        }

        if (texturePendente != null)
        {
            Destroy(texturePendente);
        }
        texturePendente = immagineCaricata;

        if (anteprimaScontrino != null)
        {
            anteprimaScontrino.texture = immagineCaricata;
        }

        AggiungiLog($"Scontrino selezionato: {Path.GetFileName(percorsoImmagine)}");
    }

    /// <summary>
    /// Gestisce il click sul pulsante di selezione del backup ZIP: permette di scegliere un
    /// file ZIP esistente diverso da quello predefinito (Application.persistentDataPath).
    /// </summary>
    private void OnClickSelezionaBackupZip()
    {
        if (elaborazioneInCorso)
        {
            AggiungiLog("Elaborazione già in corso: attendere il completamento.");
            return;
        }

        string percorsoZip = ApriSelettoreFile("Seleziona backup ZIP del database", "Backup ZIP", "zip");
        if (string.IsNullOrEmpty(percorsoZip))
        {
            // L'utente ha annullato la selezione: nessuna azione da eseguire.
            return;
        }

        gestoreDatabase.ImpostaPercorsoZipPersonalizzato(percorsoZip);
        GestoreConfigurazione.SalvaUltimoPercorsoZip(percorsoZip);

        if (testoPercorsoBackupZip != null)
        {
            testoPercorsoBackupZip.text = $"Backup: {percorsoZip}";
        }

        AggiungiLog($"Backup ZIP selezionato: {percorsoZip}");
    }

    /// <summary>
    /// Gestisce il click sul pulsante "Elabora Scontrino con AI": avvia l'analisi tramite
    /// Anthropic e aggiorna il database estratto in cache con l'esito, mostrandone
    /// l'anteprima a schermo. Il file ZIP di backup originale NON viene toccato: la scrittura
    /// definitiva avviene solo cliccando "Salva Modifiche nello ZIP" (flusso a due fasi).
    /// </summary>
    private void OnClickElaboraConAI()
    {
        if (elaborazioneInCorso)
        {
            AggiungiLog("Elaborazione già in corso: attendere il completamento.");
            return;
        }

        if (!servizioAnthropic.ChiaveApiKeyConfigurata)
        {
            AggiungiLog("Inserisci e salva la chiave API Anthropic prima di procedere.");
            MostraPannelloApiKey(true);
            return;
        }

        if (texturePendente == null)
        {
            AggiungiLog("Seleziona prima un'immagine di scontrino.");
            return;
        }

        AvviaElaborazioneScontrino(texturePendente);
    }

    /// <summary>
    /// Gestisce il click sul pulsante "Salva Modifiche nello ZIP": ricompatta nel backup ZIP
    /// finale il database estratto (già aggiornato dall'ultima elaborazione andata a buon
    /// fine), completando la seconda fase del flusso. Non ripete l'analisi né il confronto
    /// con il database: si limita a scrivere su disco quanto già calcolato.
    /// </summary>
    private void OnClickSalvaModificheZip()
    {
        if (elaborazioneInCorso)
        {
            AggiungiLog("Elaborazione già in corso: attendere il completamento.");
            return;
        }

        try
        {
            gestoreDatabase.RicomprimiDatabaseInZip();

            // Percorso assoluto e "canonico" del backup ZIP appena riscritto, così l'utente
            // sa sempre con precisione dove si trova il file aggiornato sul disco.
            string percorsoZipAssoluto = Path.GetFullPath(gestoreDatabase.PercorsoZipCorrente);
            AggiungiLog($"Modifiche salvate con successo: database ricompattato in {percorsoZipAssoluto}");

            if (testoPercorsoBackupZip != null)
            {
                testoPercorsoBackupZip.text = $"Backup: {percorsoZipAssoluto}";
            }
        }
        catch (Exception eccezione)
        {
            AggiungiLog($"Errore durante il salvataggio nello ZIP: {eccezione.Message}");
        }
    }

    /// <summary>
    /// Gestisce il click sul pulsante "Salva" del pannello chiave API: valida e persiste la
    /// chiave inserita dall'utente, rendendola immediatamente disponibile al servizio Anthropic.
    /// </summary>
    private void OnClickSalvaApiKey()
    {
        string chiave = campoApiKey != null ? campoApiKey.text.Trim() : string.Empty;
        if (string.IsNullOrEmpty(chiave))
        {
            AggiungiLog("Inserisci una chiave API valida prima di salvare.");
            return;
        }

        servizioAnthropic.ImpostaApiKey(chiave);
        GestoreConfigurazione.SalvaApiKey(chiave);
        MostraPannelloApiKey(false);
        AggiungiLog("Chiave API Anthropic salvata.");
    }

    /// <summary>
    /// Riporta l'interfaccia allo stato iniziale: pulisce l'anteprima dello scontrino, svuota
    /// le tre liste prodotti, azzera i contatori e il log, e rimuove anche l'eventuale backup
    /// ZIP personalizzato selezionato (torna al percorso predefinito) — sia dallo stato di
    /// sessione sia dalla preferenza persistita in PlayerPrefs, così che anche un riavvio
    /// dell'app dopo il Reset riparta senza alcun backup precedentemente ricordato — riportando
    /// l'intera applicazione a uno stato totalmente pulito e vergine. Tutti i pulsanti restano
    /// sempre abilitati (nessuna condizione dinamica da ricalcolare).
    /// </summary>
    private void OnClickReset()
    {
        if (texturePendente != null)
        {
            Destroy(texturePendente);
            texturePendente = null;
        }

        if (anteprimaScontrino != null)
        {
            anteprimaScontrino.texture = null;
        }

        gestoreDatabase.RimuoviPercorsoZipPersonalizzato();
        GestoreConfigurazione.SalvaUltimoPercorsoZip(string.Empty);

        if (testoPercorsoBackupZip != null)
        {
            testoPercorsoBackupZip.text = TestoBackupPredefinito;
        }

        contatoreAggiunti = 0;
        contatoreAggiornati = 0;
        contatoreInvariati = 0;
        AggiornaContatoriUI();

        if (testoLog != null)
        {
            testoLog.text = string.Empty;
        }

        if (testoListaAggiunti != null)
        {
            testoListaAggiunti.text = string.Empty;
        }

        if (testoListaAggiornati != null)
        {
            testoListaAggiornati.text = string.Empty;
        }

        if (testoListaInvariati != null)
        {
            testoListaInvariati.text = string.Empty;
        }

        AggiungiLog("Stato reimpostato.");
    }

    /// <summary>
    /// Coordina l'intero flusso: invio dell'immagine ad Anthropic, estrazione del database
    /// dallo ZIP di backup, aggiornamento dei prodotti e ricompattazione del backup.
    /// </summary>
    private void AvviaElaborazioneScontrino(Texture2D immagineScontrino)
    {
        ImpostaStatoCaricamento(true);
        AggiungiLog("Invio dello scontrino all'API Anthropic in corso...");

        servizioAnthropic.AnalizzaScontrino(
            immagineScontrino,
            alSuccesso: OnProdottiRiconosciuti,
            alFallimento: OnErroreAnthropic,
            suMessaggio: AggiungiLog);
    }

    /// <summary>
    /// Callback invocata quando Anthropic restituisce con successo l'elenco dei prodotti:
    /// aggiorna il database SQLite estratto in cache (senza toccare lo ZIP di backup
    /// originale) e mostra a schermo l'anteprima dei risultati. La scrittura definitiva nello
    /// ZIP avviene solo in un secondo momento, cliccando "Salva Modifiche nello ZIP".
    /// </summary>
    private void OnProdottiRiconosciuti(List<ProdottoScontrino> prodotti)
    {
        AggiungiLog($"Riconosciuti {prodotti.Count} prodotti nello scontrino.");

        try
        {
            gestoreDatabase.EstraiDatabaseDaZip();
            AggiungiLog("Database estratto dal backup ZIP.");

            RisultatoAggiornamentoDatabase risultato = gestoreDatabase.AggiornaProdotti(prodotti);

            contatoreAggiunti += risultato.ProdottiAggiunti;
            contatoreAggiornati += risultato.ProdottiAggiornati;
            contatoreInvariati += risultato.ProdottiInvariati;
            AggiornaContatoriUI();

            AggiornaListeProdotti(risultato.Dettagli);

            AggiungiLog($"Elaborazione completata: {risultato.ProdottiAggiunti} aggiunti, " +
                        $"{risultato.ProdottiAggiornati} aggiornati, " +
                        $"{risultato.ProdottiInvariati} invariati. " +
                        "Premi \"Salva Modifiche nello ZIP\" per confermare sul backup.");
        }
        catch (Exception eccezione)
        {
            AggiungiLog($"Errore durante l'aggiornamento del database: {eccezione.Message}");
        }
        finally
        {
            ImpostaStatoCaricamento(false);
        }
    }

    /// <summary>Callback invocata in caso di errore nella chiamata all'API Anthropic.</summary>
    private void OnErroreAnthropic(string messaggioErrore)
    {
        AggiungiLog($"Errore API Anthropic: {messaggioErrore}");
        ImpostaStatoCaricamento(false);
    }

    /// <summary>
    /// Aggiorna le tre scrollview separate (Aggiunti, Aggiornati, Invariati) con l'esito
    /// dell'ultima elaborazione, sostituendo il contenuto precedente.
    /// </summary>
    private void AggiornaListeProdotti(List<DettaglioProdottoElaborato> dettagli)
    {
        if (testoListaAggiunti != null)
        {
            testoListaAggiunti.text = CostruisciListaPerEsito(
                dettagli, EsitoProdotto.Aggiunto, dettaglio => $"{dettaglio.Nome} - €{dettaglio.Prezzo:0.00}");
        }

        if (testoListaAggiornati != null)
        {
            // "->" invece della freccia unicode "➔": il font SDF di default di TextMeshPro non
            // include quel glifo e lo renderizzerebbe come quadratino "mancante glifo" (☐).
            testoListaAggiornati.text = CostruisciListaPerEsito(
                dettagli, EsitoProdotto.Aggiornato,
                dettaglio => $"{dettaglio.Nome} - Vecchio: €{dettaglio.PrezzoPrecedente:0.00} -> Nuovo: €{dettaglio.Prezzo:0.00}");
        }

        if (testoListaInvariati != null)
        {
            testoListaInvariati.text = CostruisciListaPerEsito(
                dettagli, EsitoProdotto.Invariato, dettaglio => $"{dettaglio.Nome} - €{dettaglio.Prezzo:0.00}");
        }
    }

    /// <summary>Costruisce, una riga per prodotto, il testo della scrollview relativa a un singolo esito.</summary>
    private string CostruisciListaPerEsito(
        List<DettaglioProdottoElaborato> dettagli, EsitoProdotto esito, Func<DettaglioProdottoElaborato, string> formattaRiga)
    {
        var costruttore = new StringBuilder();
        foreach (DettaglioProdottoElaborato dettaglio in dettagli)
        {
            if (dettaglio.Esito != esito)
            {
                continue;
            }

            if (costruttore.Length > 0)
            {
                costruttore.AppendLine();
            }
            costruttore.Append(formattaRiga(dettaglio));
        }

        return costruttore.ToString();
    }

    /// <summary>
    /// Apre un selettore di file generico per scegliere un file con le estensioni indicate.
    /// In Editor utilizza il pannello nativo di Unity; nella build Standalone Windows utilizza
    /// invece la finestra "Apri file" nativa del sistema operativo (comdlg32.dll), poiché
    /// l'assembly UnityEditor non è disponibile al di fuori dell'Editor.
    /// </summary>
    private string ApriSelettoreFile(string titolo, string descrizioneFiltro, string estensioni)
    {
#if UNITY_EDITOR
        return EditorUtility.OpenFilePanel(titolo, "", estensioni);
#elif UNITY_STANDALONE_WIN
        return ApriSelettoreFileNativoConProtezioneClickThrough(titolo, descrizioneFiltro, estensioni);
#else
        AggiungiLog("Selezione file non disponibile su questa piattaforma.");
        return null;
#endif
    }

#if !UNITY_EDITOR && UNITY_STANDALONE_WIN
    /// <summary>
    /// Avvolge <see cref="SelettoreFileNativoWindows.ApriFileDialog"/> con una protezione contro
    /// il "click-through": in Windows, il click con cui l'utente conferma "Apri/OK" chiude la
    /// finestra di dialogo nativa, ma quello stesso evento di mouse può "passare attraverso" e
    /// raggiungere la finestra Unity sottostante non appena il focus torna ad essa, attivando
    /// per errore qualunque pulsante Unity si trovi sotto le coordinate del cursore in quel
    /// momento (es. "Elabora Scontrino con AI"). Per evitarlo, i raycast sulla Canvas principale
    /// vengono disattivati prima di aprire il dialog e riattivati solo dopo un breve ritardo
    /// (<see cref="RitardoRiabilitazioneRaycastSecondi"/>) dalla sua chiusura, così da assorbire
    /// l'eventuale click residuo prima che possa raggiungere un elemento della UI.
    /// </summary>
    private string ApriSelettoreFileNativoConProtezioneClickThrough(string titolo, string descrizioneFiltro, string estensioni)
    {
        if (canvasGroupPrincipale != null)
        {
            canvasGroupPrincipale.blocksRaycasts = false;
        }

        string percorso = SelettoreFileNativoWindows.ApriFileDialog(titolo, descrizioneFiltro, estensioni);

        StartCoroutine(RiabilitaRaycastCanvasDopoRitardo());

        return percorso;
    }

    private const float RitardoRiabilitazioneRaycastSecondi = 0.3f;

    /// <summary>Riattiva i raycast sulla Canvas principale dopo un breve ritardo (tempo reale, non affetto da Time.timeScale).</summary>
    private IEnumerator RiabilitaRaycastCanvasDopoRitardo()
    {
        yield return new WaitForSecondsRealtime(RitardoRiabilitazioneRaycastSecondi);

        if (canvasGroupPrincipale != null)
        {
            canvasGroupPrincipale.blocksRaycasts = true;
        }
    }
#endif

    /// <summary>
    /// Carica, all'avvio, la chiave API Anthropic e l'ultimo percorso ZIP salvati in precedenza
    /// (<see cref="GestoreConfigurazione"/>) e aggiorna la UI di conseguenza: mostra il pannello
    /// di inserimento chiave API solo se non è ancora configurata una chiave valida.
    /// </summary>
    private void CaricaConfigurazionePersistente()
    {
        string chiaveSalvata = GestoreConfigurazione.CaricaApiKey();
        if (!string.IsNullOrEmpty(chiaveSalvata))
        {
            servizioAnthropic.ImpostaApiKey(chiaveSalvata);
            if (campoApiKey != null)
            {
                campoApiKey.text = chiaveSalvata;
            }
        }

        MostraPannelloApiKey(!servizioAnthropic.ChiaveApiKeyConfigurata);

        string ultimoPercorsoZip = GestoreConfigurazione.CaricaUltimoPercorsoZip();
        if (!string.IsNullOrEmpty(ultimoPercorsoZip) && File.Exists(ultimoPercorsoZip))
        {
            gestoreDatabase.ImpostaPercorsoZipPersonalizzato(ultimoPercorsoZip);
            if (testoPercorsoBackupZip != null)
            {
                testoPercorsoBackupZip.text = $"Backup: {ultimoPercorsoZip}";
            }
        }
    }

    /// <summary>Mostra o nasconde il pannello di inserimento della chiave API Anthropic.</summary>
    private void MostraPannelloApiKey(bool mostra)
    {
        if (pannelloApiKey != null)
        {
            pannelloApiKey.SetActive(mostra);
        }
    }

    /// <summary>Carica un file immagine dal disco in una Texture2D.</summary>
    private Texture2D CaricaTextureDaFile(string percorsoFile)
    {
        if (!File.Exists(percorsoFile))
        {
            return null;
        }

        byte[] datiFile = File.ReadAllBytes(percorsoFile);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(datiFile))
        {
            Destroy(texture);
            return null;
        }

        return texture;
    }

    /// <summary>Aggiorna i campi di testo dei contatori con i valori correnti.</summary>
    private void AggiornaContatoriUI()
    {
        if (testoContatoreAggiunti != null)
        {
            testoContatoreAggiunti.text = $"Aggiunti: {contatoreAggiunti}";
        }

        if (testoContatoreAggiornati != null)
        {
            testoContatoreAggiornati.text = $"Aggiornati: {contatoreAggiornati}";
        }

        if (testoContatoreInvariati != null)
        {
            testoContatoreInvariati.text = $"Invariati: {contatoreInvariati}";
        }
    }

    /// <summary>Aggiunge una riga con timestamp al log visualizzato a schermo.</summary>
    private void AggiungiLog(string messaggio)
    {
        string riga = $"[{DateTime.Now:HH:mm:ss}] {messaggio}";
        Debug.Log(riga);

        if (testoLog != null)
        {
            var costruttore = new StringBuilder(testoLog.text);
            if (costruttore.Length > 0)
            {
                costruttore.AppendLine();
            }
            costruttore.Append(riga);
            testoLog.text = costruttore.ToString();
        }
    }

    /// <summary>
    /// Traccia se un'elaborazione è in corso (per evitare avvii concorrenti dagli handler dei
    /// pulsanti) e mostra/nasconde il relativo indicatore di caricamento. Semplificazione
    /// temporanea: tutti i pulsanti restano sempre abilitati (interactable = true) e questo
    /// metodo non ne modifica più l'interattività.
    /// </summary>
    private void ImpostaStatoCaricamento(bool inCorso)
    {
        elaborazioneInCorso = inCorso;

        if (indicatoreCaricamento != null)
        {
            indicatoreCaricamento.SetActive(inCorso);
        }
    }
}
