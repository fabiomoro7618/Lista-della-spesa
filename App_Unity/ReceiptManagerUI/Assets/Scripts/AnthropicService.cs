using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Rappresenta un singolo prodotto individuato nello scontrino dall'API Anthropic.
/// </summary>
[Serializable]
public class ProdottoScontrino
{
    public string nome;
    public float prezzo;
    public int quantita = 1;
}

/// <summary>
/// Servizio responsabile della comunicazione con l'API Anthropic (Claude) per l'analisi
/// di immagini di scontrini. Deve essere agganciato a un GameObject nella scena poiché
/// utilizza le coroutine di Unity per gestire la richiesta HTTP in modo asincrono.
/// Richiede il pacchetto "com.unity.nuget.newtonsoft-json" (Package Manager) per la
/// serializzazione/deserializzazione JSON.
/// </summary>
public class AnthropicService : MonoBehaviour
{
    private const string EndpointApi = "https://api.anthropic.com/v1/messages";
    private const string VersioneApi = "2023-06-01";
    private const string ModelloUtilizzato = "claude-opus-5";

    [Header("Configurazione API Anthropic")]
    [Tooltip("Chiave API Anthropic. NON committare mai una chiave reale nel controllo versione: " +
             "valorizzare a runtime da un file di configurazione locale o da una variabile d'ambiente.")]
    [SerializeField]
    private string apiKey;

    [Tooltip("Numero massimo di token generati in risposta. Un valore contenuto è sufficiente " +
             "poiché la risposta attesa è un semplice elenco JSON di prodotti.")]
    [SerializeField]
    private int maxToken = 4096;

    [Header("Compressione immagine")]
    [Tooltip("Lato più lungo massimo (in pixel) a cui ridimensionare l'immagine prima dell'invio. " +
             "Le fotocamere moderne producono immagini molto più grandi del necessario per un OCR: " +
             "ridurle evita di superare il limite di 10 MB per immagine imposto dall'API Anthropic.")]
    [SerializeField]
    private int latoMassimoImmagine = 2048;

    [Tooltip("Qualità di compressione JPEG (1-100) usata per l'invio. 75-80 offre una qualità " +
             "visiva eccellente per l'OCR con un peso di pochi megabyte.")]
    [SerializeField]
    [Range(1, 100)]
    private int qualitaJpeg = 80;

    // Margine di sicurezza tenuto ben al di sotto del limite di 10 MB imposto dall'API Anthropic
    // per i blocchi immagine, per assorbire l'overhead di codifica Base64 (~+33%) e variazioni tra scontrini.
    private const long DimensioneMassimaImmagineBytes = 8L * 1024 * 1024;
    private const int QualitaJpegMinima = 30;

    [Header("Resilienza rete")]
    [Tooltip("Numero massimo di tentativi complessivi (incluso il primo) in caso di errori di " +
             "rete, timeout o codici di stato temporanei (429, 500, 502, 503, 504).")]
    [SerializeField]
    private int numeroMassimoTentativi = 3;

    [Tooltip("Timeout (in secondi) applicato a ciascun singolo tentativo di richiesta HTTP.")]
    [SerializeField]
    private int timeoutSecondiPerTentativo = 45;

    [Tooltip("Attesa (in secondi) prima del primo nuovo tentativo dopo un errore temporaneo; " +
             "raddoppia ad ogni tentativo successivo (backoff esponenziale: 2s, 4s, 8s, ...).")]
    [SerializeField]
    private float attesaBaseSecondiRetry = 2f;

    // Codici di stato HTTP considerati temporanei e quindi validi per un nuovo tentativo:
    // 429 (rate limit), 500/502/503/504 (errori lato server, tipicamente transitori).
    private static readonly HashSet<long> CodiciStatoTemporanei = new HashSet<long> { 429, 500, 502, 503, 504 };

    /// <summary>
    /// Permette di impostare la chiave API a runtime (es. caricata da un file di configurazione
    /// non versionato) invece di esporla nell'Inspector.
    /// </summary>
    public void ImpostaApiKey(string chiave)
    {
        apiKey = chiave;
    }

    /// <summary>Indica se è attualmente configurata una chiave API valida (Inspector o runtime).</summary>
    public bool ChiaveApiKeyConfigurata => !string.IsNullOrEmpty(apiKey);

    /// <summary>
    /// Avvia l'elaborazione asincrona di un'immagine di scontrino tramite l'API Anthropic.
    /// In caso di errori di rete, timeout o codici di stato temporanei, la richiesta viene
    /// ritentata automaticamente (fino a <see cref="numeroMassimoTentativi"/> tentativi) con
    /// attesa a backoff esponenziale tra un tentativo e l'altro.
    /// </summary>
    /// <param name="immagineScontrino">Texture dello scontrino selezionato dall'utente.</param>
    /// <param name="alSuccesso">Callback invocata con l'elenco dei prodotti riconosciuti.</param>
    /// <param name="alFallimento">Callback invocata con un messaggio di errore definitivo (dopo aver esaurito i tentativi).</param>
    /// <param name="suMessaggio">Callback opzionale invocata con messaggi di avanzamento (es. avvisi di retry), utile per il log a schermo.</param>
    public void AnalizzaScontrino(
        Texture2D immagineScontrino,
        Action<List<ProdottoScontrino>> alSuccesso,
        Action<string> alFallimento,
        Action<string> suMessaggio = null)
    {
        if (immagineScontrino == null)
        {
            alFallimento?.Invoke("Nessuna immagine fornita per l'analisi.");
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            alFallimento?.Invoke("Chiave API Anthropic non configurata.");
            return;
        }

        StartCoroutine(EseguiRichiestaScontrino(immagineScontrino, alSuccesso, alFallimento, suMessaggio));
    }

    /// <summary>
    /// Coroutine che costruisce la richiesta JSON, invia l'immagine in Base64 all'API Anthropic
    /// e interpreta la risposta restituendo l'elenco dei prodotti trovati. Gestisce internamente
    /// i tentativi ripetuti con backoff esponenziale in caso di errori temporanei.
    /// </summary>
    private IEnumerator EseguiRichiestaScontrino(
        Texture2D immagine,
        Action<List<ProdottoScontrino>> alSuccesso,
        Action<string> alFallimento,
        Action<string> suMessaggio)
    {
        // Ridimensiona (se necessario) e comprime in JPEG l'immagine prima di codificarla in
        // Base64: evita l'errore "Image exceeds 10 MB maximum" restituito dall'API Anthropic
        // per scontrini fotografati ad alta risoluzione. Il payload non cambia tra un tentativo
        // e l'altro, quindi viene preparato una sola volta prima del ciclo di retry.
        byte[] bytesImmagine = PreparaImmaginePerInvio(immagine);
        string immagineBase64 = Convert.ToBase64String(bytesImmagine);
        string corpoRichiesta = CostruisciCorpoRichiesta(immagineBase64);
        byte[] bytesCorpo = Encoding.UTF8.GetBytes(corpoRichiesta);

        for (int tentativo = 1; tentativo <= numeroMassimoTentativi; tentativo++)
        {
            bool successo = false;
            bool erroreTemporaneo = false;
            string corpoRisposta = null;
            string messaggioErrore = null;

            // La UnityWebRequest (e i relativi upload/download handler) viene creata e chiusa
            // interamente all'interno di questo blocco "using": ad ogni tentativo, comprese le
            // ripetizioni, le risorse HTTP della richiesta precedente sono già state liberate.
            using (UnityWebRequest richiesta = new UnityWebRequest(EndpointApi, UnityWebRequest.kHttpVerbPOST))
            {
                richiesta.uploadHandler = new UploadHandlerRaw(bytesCorpo);
                richiesta.downloadHandler = new DownloadHandlerBuffer();
                richiesta.SetRequestHeader("content-type", "application/json");
                richiesta.SetRequestHeader("x-api-key", apiKey);
                richiesta.SetRequestHeader("anthropic-version", VersioneApi);
                richiesta.timeout = timeoutSecondiPerTentativo;

                yield return richiesta.SendWebRequest();

                if (richiesta.result == UnityWebRequest.Result.Success)
                {
                    successo = true;
                    corpoRisposta = richiesta.downloadHandler.text;
                }
                else
                {
                    erroreTemporaneo = EErroreTemporaneo(richiesta);
                    string dettagliServer = EstraiMessaggioErrore(richiesta.downloadHandler != null ? richiesta.downloadHandler.text : null);
                    messaggioErrore = $"Errore chiamata API Anthropic ({richiesta.responseCode}): {dettagliServer ?? richiesta.error}";
                }
            }

            if (successo)
            {
                List<ProdottoScontrino> prodottiTrovati;
                try
                {
                    prodottiTrovati = InterpretaRisposta(corpoRisposta);
                }
                catch (Exception eccezione)
                {
                    alFallimento?.Invoke($"Errore durante l'interpretazione della risposta API: {eccezione.Message}");
                    yield break;
                }

                alSuccesso?.Invoke(prodottiTrovati);
                yield break;
            }

            bool possoRiprovare = erroreTemporaneo && tentativo < numeroMassimoTentativi;
            if (!possoRiprovare)
            {
                string prefisso = erroreTemporaneo
                    ? $"⚠️ Connessione instabile: falliti tutti i {numeroMassimoTentativi} tentativi. "
                    : string.Empty;
                alFallimento?.Invoke(prefisso + messaggioErrore);
                yield break;
            }

            // Backoff esponenziale: 2s dopo il 1° fallimento, 4s dopo il 2°, ecc.
            float attesaSecondi = attesaBaseSecondiRetry * Mathf.Pow(2f, tentativo - 1);
            suMessaggio?.Invoke(
                $"⚠️ Connessione instabile. Tentativo {tentativo + 1}/{numeroMassimoTentativi} tra {attesaSecondi:0} secondi...");

            yield return new WaitForSeconds(attesaSecondi);
        }
    }

    /// <summary>
    /// Determina se l'esito di una richiesta è da considerarsi temporaneo (e quindi degno di un
    /// nuovo tentativo): errori di connessione/rete/timeout, oppure codici di stato HTTP transitori
    /// (429, 500, 502, 503, 504). Errori di protocollo diversi (es. 400, 401, 403, 404) indicano
    /// invece un problema nella richiesta stessa: ritentare non servirebbe a nulla.
    /// </summary>
    private bool EErroreTemporaneo(UnityWebRequest richiesta)
    {
        if (richiesta.result == UnityWebRequest.Result.ConnectionError)
        {
            // Copre errori di rete, DNS, connessione interrotta a metà trasferimento
            // (es. "Curl error 56") e timeout: sono per definizione transitori.
            return true;
        }

        if (richiesta.result == UnityWebRequest.Result.ProtocolError)
        {
            return CodiciStatoTemporanei.Contains(richiesta.responseCode);
        }

        return false;
    }

    /// <summary>
    /// Ridimensiona (se il lato più lungo supera <see cref="latoMassimoImmagine"/>) e comprime
    /// in JPEG l'immagine dello scontrino prima dell'invio, per rimanere sotto il limite di
    /// 10 MB imposto dall'API Anthropic per i blocchi immagine. Non modifica né distrugge la
    /// texture originale passata come parametro (potrebbe essere ancora mostrata in anteprima
    /// nell'interfaccia utente).
    /// </summary>
    private byte[] PreparaImmaginePerInvio(Texture2D originale)
    {
        Texture2D sorgente = originale;
        Texture2D copiaRidimensionata = null;

        int latoLungoOriginale = Mathf.Max(originale.width, originale.height);
        if (latoLungoOriginale > latoMassimoImmagine)
        {
            copiaRidimensionata = RidimensionaTexture(originale, latoMassimoImmagine);
            sorgente = copiaRidimensionata;
        }

        int qualitaCorrente = qualitaJpeg;
        byte[] bytesJpeg = sorgente.EncodeToJPG(qualitaCorrente);

        // Se, nonostante ridimensionamento e compressione, l'immagine è ancora troppo pesante
        // (es. scontrino molto lungo o particolarmente dettagliato), riduce ulteriormente la
        // qualità JPEG a step, fino a rientrare nel margine di sicurezza o al minimo consentito.
        while (bytesJpeg.Length > DimensioneMassimaImmagineBytes && qualitaCorrente > QualitaJpegMinima)
        {
            qualitaCorrente = Mathf.Max(qualitaCorrente - 15, QualitaJpegMinima);
            bytesJpeg = sorgente.EncodeToJPG(qualitaCorrente);
        }

        if (copiaRidimensionata != null)
        {
            Destroy(copiaRidimensionata);
        }

        if (bytesJpeg.Length > DimensioneMassimaImmagineBytes)
        {
            Debug.LogWarning(
                $"[AnthropicService] L'immagine compressa pesa ancora {bytesJpeg.Length / 1024 / 1024} MB " +
                "dopo ridimensionamento e riduzione della qualità JPEG: l'API potrebbe comunque rifiutarla.");
        }

        return bytesJpeg;
    }

    /// <summary>
    /// Restituisce una nuova Texture2D ridimensionata mantenendo le proporzioni originali, con
    /// il lato più lungo pari a <paramref name="latoMassimo"/>. La texture originale non viene
    /// modificata né distrutta: la copia ridimensionata va distrutta esplicitamente dal chiamante
    /// quando non più necessaria.
    /// </summary>
    private Texture2D RidimensionaTexture(Texture2D originale, int latoMassimo)
    {
        float scala = (float)latoMassimo / Mathf.Max(originale.width, originale.height);
        int nuovaLarghezza = Mathf.Max(1, Mathf.RoundToInt(originale.width * scala));
        int nuovaAltezza = Mathf.Max(1, Mathf.RoundToInt(originale.height * scala));

        RenderTexture renderTextureTemporanea = RenderTexture.GetTemporary(nuovaLarghezza, nuovaAltezza, 0, RenderTextureFormat.ARGB32);
        RenderTexture renderTextureAttivaPrecedente = RenderTexture.active;

        Graphics.Blit(originale, renderTextureTemporanea);
        RenderTexture.active = renderTextureTemporanea;

        var textureRidimensionata = new Texture2D(nuovaLarghezza, nuovaAltezza, TextureFormat.RGB24, false);
        textureRidimensionata.ReadPixels(new Rect(0, 0, nuovaLarghezza, nuovaAltezza), 0, 0);
        textureRidimensionata.Apply();

        RenderTexture.active = renderTextureAttivaPrecedente;
        RenderTexture.ReleaseTemporary(renderTextureTemporanea);

        return textureRidimensionata;
    }

    /// <summary>
    /// Costruisce il corpo JSON della richiesta verso l'endpoint "messages", includendo
    /// il prompt di sistema e il blocco immagine in Base64.
    /// </summary>
    private string CostruisciCorpoRichiesta(string immagineBase64)
    {
        // Il prompt di sistema istruisce il modello a rispondere ESCLUSIVAMENTE con un array
        // JSON valido, così da poterlo interpretare senza ambiguità lato client.
        const string promptSistema =
            "Sei un assistente specializzato nell'estrazione di dati da scontrini fiscali. " +
            "Analizza l'immagine fornita ed estrai l'elenco dei prodotti acquistati. " +
            "Rispondi ESCLUSIVAMENTE con un array JSON valido (nessun testo aggiuntivo, nessun markdown), " +
            "nel seguente formato: " +
            "[{\"nome\": \"nome del prodotto\", \"prezzo\": 0.00, \"quantita\": 1}]. " +
            "Usa il punto come separatore decimale per il prezzo. " +
            "Se la quantità non è desumibile, usa 1.";

        var corpo = new JObject
        {
            ["model"] = ModelloUtilizzato,
            ["max_tokens"] = maxToken,
            ["system"] = promptSistema,
            ["messages"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "image",
                            ["source"] = new JObject
                            {
                                ["type"] = "base64",
                                ["media_type"] = "image/jpeg",
                                ["data"] = immagineBase64
                            }
                        },
                        new JObject
                        {
                            ["type"] = "text",
                            ["text"] = "Analizza questo scontrino ed estrai l'elenco dei prodotti con i relativi prezzi."
                        }
                    }
                }
            }
        };

        return corpo.ToString(Formatting.None);
    }

    /// <summary>
    /// Estrae il testo generato dal modello dalla risposta dell'API e lo converte
    /// nell'elenco tipizzato dei prodotti riconosciuti nello scontrino.
    /// </summary>
    private List<ProdottoScontrino> InterpretaRisposta(string corpoRisposta)
    {
        JObject rispostaJson = JObject.Parse(corpoRisposta);

        // Se il modello ha rifiutato la richiesta, "content" può essere vuoto: gestiamo il caso esplicitamente.
        JArray blocchiContenuto = rispostaJson["content"] as JArray;
        if (blocchiContenuto == null || blocchiContenuto.Count == 0)
        {
            string motivoArresto = rispostaJson["stop_reason"]?.ToString();
            throw new Exception($"Risposta priva di contenuto testuale (stop_reason: {motivoArresto}).");
        }

        // Il testo generato dal modello è nel primo blocco di tipo "text".
        string testoGenerato = null;
        foreach (JToken blocco in blocchiContenuto)
        {
            if (blocco["type"]?.ToString() == "text")
            {
                testoGenerato = blocco["text"]?.ToString();
                break;
            }
        }

        if (string.IsNullOrEmpty(testoGenerato))
        {
            throw new Exception("Nessun blocco di testo trovato nella risposta del modello.");
        }

        // Rimuove eventuali delimitatori markdown (```json ... ```) che il modello potrebbe
        // aggiungere nonostante le istruzioni del prompt di sistema.
        testoGenerato = PulisciTestoJson(testoGenerato);

        return JsonConvert.DeserializeObject<List<ProdottoScontrino>>(testoGenerato) ?? new List<ProdottoScontrino>();
    }

    /// <summary>
    /// Rimuove eventuali fence markdown ```json ... ``` dal testo restituito dal modello.
    /// </summary>
    private string PulisciTestoJson(string testo)
    {
        string risultato = testo.Trim();
        if (risultato.StartsWith("```"))
        {
            int primoAndataCapo = risultato.IndexOf('\n');
            if (primoAndataCapo >= 0)
            {
                risultato = risultato.Substring(primoAndataCapo + 1);
            }
            int indiceChiusura = risultato.LastIndexOf("```", StringComparison.Ordinal);
            if (indiceChiusura >= 0)
            {
                risultato = risultato.Substring(0, indiceChiusura);
            }
        }
        return risultato.Trim();
    }

    /// <summary>
    /// Tenta di estrarre un messaggio di errore leggibile dal corpo di una risposta HTTP non valida.
    /// </summary>
    private string EstraiMessaggioErrore(string corpoRisposta)
    {
        if (string.IsNullOrEmpty(corpoRisposta))
        {
            return null;
        }

        try
        {
            JObject errore = JObject.Parse(corpoRisposta);
            return errore["error"]?["message"]?.ToString();
        }
        catch (JsonException)
        {
            return corpoRisposta;
        }
    }
}
