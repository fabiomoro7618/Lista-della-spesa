#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Script Editor che genera automaticamente, all'interno della scena SampleScene.unity,
/// l'intera interfaccia utente per la gestione degli scontrini e del backup del database:
/// Canvas (1920x1080) + EventSystem, pannello principale con titolo, pulsante di chiusura in
/// alto a destra, pulsanti di selezione file, area di log scorrevole, contatori
/// Aggiunti/Aggiornati/Invariati e un GameObject "[AppController]" con
/// <see cref="ReceiptUIController"/>, <see cref="AnthropicService"/>,
/// <see cref="ZipAndDatabaseManager"/> e <see cref="GestoreApplicazione"/> già collegati e con
/// tutti i riferimenti UI assegnati.
///
/// Uso: menu Tools -> Build Receipt UI Scene.
///
/// Lo script è idempotente: rieseguendolo, la gerarchia generata in precedenza (Canvas e
/// AppController) viene rimossa e ricreata da zero, senza lasciare duplicati.
/// </summary>
public static class UISceneBuilder
{
    private const string PercorsoScena = "Assets/Scenes/SampleScene.unity";

    private const string NomeCanvas = "ReceiptUICanvas";
    private const string NomeAppController = "[AppController]";

    private const float MargineLayout = 40f;
    private const float SpaziaturaLayout = 16f;

    [MenuItem("Tools/Build Receipt UI Scene")]
    public static void CostruisciScenaUI()
    {
        try
        {
            Scene scena = ApriScenaTarget();

            RimuoviGerarchiaPrecedente(scena);

            EventSystem eventSystem = AssicuraEventSystem();

            // --- Canvas e struttura di layout principale ---
            Canvas canvas = CreaCanvas();

            // CanvasGroup usato da ReceiptUIController per bloccare temporaneamente i raycast
            // sull'intera UI subito dopo la chiusura del file picker nativo di Windows, contro
            // il click "passante" (click-through) che altrimenti attiverebbe per errore il
            // pulsante Unity sottostante alle coordinate del cursore.
            CanvasGroup canvasGroupPrincipale = canvas.gameObject.AddComponent<CanvasGroup>();

            RectTransform pannello = CreaPannelloPrincipale(canvas.transform);
            RectTransform layout = CreaLayoutPrincipale(pannello);

            // Pulsante di chiusura: creato come figlio diretto del Canvas (non del layout
            // verticale) e ancorato in alto a destra, così resta sempre visibile sopra al
            // resto dell'interfaccia indipendentemente dal contenuto del pannello principale.
            Button pulsanteChiudi = CreaPulsanteChiudi(canvas.transform);

            CreaTitolo(layout);

            (GameObject pannelloApiKey, TMP_InputField campoApiKey, Button pulsanteSalvaApiKey) =
                CreaPannelloApiKey(layout);

            RectTransform rigaSuperiore = CreaRigaSuperiore(layout);
            RawImage anteprima = CreaAnteprimaScontrino(rigaSuperiore);
            RectTransform colonnaPulsanti = CreaColonnaPulsanti(rigaSuperiore);

            Button pulsanteSelezionaScontrino = CreaPulsante(
                colonnaPulsanti, "PulsanteSelezionaScontrino", "Seleziona Scontrino", 64f,
                new Color(0.20f, 0.45f, 0.85f));

            Button pulsanteSelezionaBackupZip = CreaPulsante(
                colonnaPulsanti, "PulsanteSelezionaBackupZip", "Seleziona Backup ZIP", 64f,
                new Color(0.35f, 0.35f, 0.42f));

            TMP_Text testoPercorsoBackupZip = CreaTestoInformativo(
                colonnaPulsanti, "TestoPercorsoBackupZip", "Backup: predefinito (Application.persistentDataPath)");

            // Semplificazione temporanea: tutti i pulsanti sono sempre abilitati, senza
            // condizioni dinamiche di interactable (vedi ReceiptUIController.ImpostaStatoCaricamento).
            Button pulsanteElaboraConAI = CreaPulsante(
                colonnaPulsanti, "PulsanteElaboraConAI", "Elabora Scontrino con AI", 76f,
                new Color(0.15f, 0.65f, 0.35f));

            Button pulsanteSalvaModificheZip = CreaPulsante(
                colonnaPulsanti, "PulsanteSalvaModificheZip", "Salva Modifiche nello ZIP", 64f,
                new Color(0.15f, 0.45f, 0.65f));

            Button pulsanteReset = CreaPulsante(
                colonnaPulsanti, "PulsanteReset", "Reset", 48f,
                new Color(0.55f, 0.22f, 0.22f));

            RectTransform rigaContatori = CreaRigaContatori(layout);
            TMP_Text testoAggiunti = CreaContatore(rigaContatori, "TestoContatoreAggiunti", "Aggiunti: 0", new Color(0.30f, 0.80f, 0.40f));
            TMP_Text testoAggiornati = CreaContatore(rigaContatori, "TestoContatoreAggiornati", "Aggiornati: 0", new Color(0.95f, 0.75f, 0.25f));
            TMP_Text testoInvariati = CreaContatore(rigaContatori, "TestoContatoreInvariati", "Invariati: 0", new Color(0.70f, 0.70f, 0.70f));

            // Tre scrollview separate, posizionate subito sotto la riga dei contatori e
            // allineate con essi: una per ciascun esito (aggiunti, aggiornati, invariati).
            (TMP_Text testoListaAggiunti, TMP_Text testoListaAggiornati, TMP_Text testoListaInvariati) =
                CreaRigaListeProdotti(layout);

            GameObject indicatoreCaricamento = CreaIndicatoreCaricamento(layout);

            // Riga inferiore: colonna unica con il log delle operazioni, titolo e area di
            // testo scorrevole indipendente.
            RectTransform rigaInferiore = CreaRigaInferiore(layout);
            TMP_Text testoLog = CreaColonnaConTitolo(
                rigaInferiore, "ColonnaLog", "Log operazioni", new Color(0.85f, 0.90f, 0.85f));

            // --- GameObject controller con i quattro componenti collegati ---
            GameObject appController = new GameObject(NomeAppController);
            AnthropicService servizioAnthropic = appController.AddComponent<AnthropicService>();
            ZipAndDatabaseManager gestoreDatabase = appController.AddComponent<ZipAndDatabaseManager>();
            ReceiptUIController controller = appController.AddComponent<ReceiptUIController>();
            GestoreApplicazione gestoreApplicazione = appController.AddComponent<GestoreApplicazione>();

            CollegaRiferimenti(
                controller, servizioAnthropic, gestoreDatabase,
                pulsanteSelezionaScontrino, pulsanteSelezionaBackupZip, pulsanteElaboraConAI,
                pulsanteSalvaModificheZip, pulsanteReset,
                pannelloApiKey, campoApiKey, pulsanteSalvaApiKey,
                testoAggiunti, testoAggiornati, testoInvariati,
                testoListaAggiunti, testoListaAggiornati, testoListaInvariati,
                testoLog, testoPercorsoBackupZip, anteprima, indicatoreCaricamento,
                canvasGroupPrincipale);

            CollegaRiferimentoPulsanteChiudi(gestoreApplicazione, pulsanteChiudi);

            EditorSceneManager.MarkSceneDirty(scena);
            bool salvata = EditorSceneManager.SaveScene(scena);

            if (salvata)
            {
                Debug.Log($"[UISceneBuilder] Scena '{PercorsoScena}' generata e salvata con successo. " +
                          $"Creati: '{NomeCanvas}', '{eventSystem.gameObject.name}' (se non già presente), '{NomeAppController}'.");
            }
            else
            {
                Debug.LogWarning($"[UISceneBuilder] UI generata ma il salvataggio automatico di '{PercorsoScena}' non è riuscito: " +
                                  "salvare manualmente con File > Save (Ctrl+S).");
            }

            Debug.Log("[UISceneBuilder] IMPORTANTE: impostare manualmente la chiave API Anthropic sul componente " +
                      "AnthropicService (campo 'apiKey' su [AppController]) — non viene mai valorizzata automaticamente.");
        }
        catch (System.Exception eccezione)
        {
            Debug.LogError($"[UISceneBuilder] Errore durante la generazione della UI: {eccezione}");
            throw;
        }
    }

    // ------------------------------------------------------------------
    // Gestione scena
    // ------------------------------------------------------------------

    /// <summary>Apre (se necessario) la scena target, rendendola la scena attiva.</summary>
    private static Scene ApriScenaTarget()
    {
        Scene scenaAttiva = SceneManager.GetActiveScene();
        if (scenaAttiva.path != PercorsoScena)
        {
            scenaAttiva = EditorSceneManager.OpenScene(PercorsoScena, OpenSceneMode.Single);
        }
        return scenaAttiva;
    }

    /// <summary>
    /// Rimuove eventuali GameObject radice generati da una precedente esecuzione dello script,
    /// così da rendere l'operazione ripetibile senza creare duplicati.
    /// </summary>
    private static void RimuoviGerarchiaPrecedente(Scene scena)
    {
        foreach (GameObject radice in scena.GetRootGameObjects())
        {
            if (radice.name == NomeCanvas || radice.name == NomeAppController)
            {
                Object.DestroyImmediate(radice);
            }
        }
    }

    /// <summary>Assicura la presenza di un EventSystem in scena, creandolo se assente.</summary>
    private static EventSystem AssicuraEventSystem()
    {
        EventSystem esistente = Object.FindFirstObjectByType<EventSystem>();
        if (esistente != null)
        {
            return esistente;
        }

        GameObject go = new GameObject("EventSystem", typeof(EventSystem));

        // Il progetto usa il nuovo Input System (Project Settings > Active Input Handling):
        // il modulo dell'EventSystem deve corrispondere, altrimenti i pulsanti UI non
        // riceveranno alcun evento di click.
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif

        return go.GetComponent<EventSystem>();
    }

    // ------------------------------------------------------------------
    // Canvas e contenitori
    // ------------------------------------------------------------------

    private static Canvas CreaCanvas()
    {
        GameObject go = new GameObject(
            NomeCanvas, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.layer = LayerMask.NameToLayer("UI");

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static RectTransform CreaPannelloPrincipale(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("PannelloPrincipale", genitore);
        RectTransform rt = ImpostaRectStretchCompleto(go);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.10f, 0.11f, 0.14f, 1f);

        return rt;
    }

    private static RectTransform CreaLayoutPrincipale(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("Layout", genitore);
        RectTransform rt = ImpostaRectStretchCompleto(go);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)MargineLayout, (int)MargineLayout, (int)MargineLayout, (int)MargineLayout);
        layout.spacing = SpaziaturaLayout;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return rt;
    }

    private static TMP_Text CreaTitolo(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("TitoloApp", genitore);
        AggiungiLayoutElement(go).preferredHeight = 70f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Gestione Scontrini & Lista Spesa";
        tmp.fontSize = 54f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return tmp;
    }

    private static RectTransform CreaRigaSuperiore(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("RigaSuperiore", genitore);
        AggiungiLayoutElement(go).preferredHeight = 400f;

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SpaziaturaLayout;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        return go.GetComponent<RectTransform>();
    }

    private static RawImage CreaAnteprimaScontrino(Transform genitore)
    {
        GameObject contenitore = NuovoOggettoUI("ContenitoreAnteprima", genitore);
        var layoutElement = AggiungiLayoutElement(contenitore);
        layoutElement.preferredWidth = 420f;
        layoutElement.flexibleWidth = 0f;

        var bg = contenitore.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.35f);

        GameObject figlio = NuovoOggettoUI("AnteprimaScontrino", contenitore.transform);
        ImpostaRectStretchCompleto(figlio, margine: 8f);
        var raw = figlio.AddComponent<RawImage>();
        raw.color = Color.white;

        return raw;
    }

    private static RectTransform CreaColonnaPulsanti(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("ColonnaPulsanti", genitore);
        AggiungiLayoutElement(go).flexibleWidth = 1f;

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        return go.GetComponent<RectTransform>();
    }

    // ------------------------------------------------------------------
    // Pulsanti e testi
    // ------------------------------------------------------------------

    private static Button CreaPulsante(Transform genitore, string nome, string testo, float altezza, Color coloreBase)
    {
        GameObject go = NuovoOggettoUI(nome, genitore);
        AggiungiLayoutElement(go).preferredHeight = altezza;

        var img = go.AddComponent<Image>();
        img.color = Color.white; // il colore reale è determinato dalle transizioni ColorTint del Button

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;

        ColorBlock colori = btn.colors;
        colori.normalColor = coloreBase;
        colori.highlightedColor = Color.Lerp(coloreBase, Color.white, 0.15f);
        colori.pressedColor = Color.Lerp(coloreBase, Color.black, 0.20f);
        colori.selectedColor = coloreBase;
        colori.disabledColor = new Color(coloreBase.r, coloreBase.g, coloreBase.b, 0.35f);
        colori.colorMultiplier = 1f;
        colori.fadeDuration = 0.1f;
        btn.colors = colori;

        GameObject figlioTesto = NuovoOggettoUI("Testo", go.transform);
        ImpostaRectStretchCompleto(figlioTesto);
        var tmp = figlioTesto.AddComponent<TextMeshProUGUI>();
        tmp.text = testo;
        tmp.fontSize = 28f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return btn;
    }

    /// <summary>
    /// Crea il pulsante di chiusura dell'applicazione ("✕"), ancorato in alto a destra del
    /// Canvas indipendentemente dal layout verticale principale.
    /// </summary>
    private static Button CreaPulsanteChiudi(Transform genitoreCanvas)
    {
        const float lato = 48f;
        const float margine = 24f;

        GameObject go = NuovoOggettoUI("PulsanteChiudi", genitoreCanvas);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(lato, lato);
        rt.anchoredPosition = new Vector2(-margine, -margine);

        var img = go.AddComponent<Image>();
        img.color = Color.white;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.transition = Selectable.Transition.ColorTint;

        Color coloreBase = new Color(0.65f, 0.20f, 0.20f);
        ColorBlock colori = btn.colors;
        colori.normalColor = coloreBase;
        colori.highlightedColor = Color.Lerp(coloreBase, Color.white, 0.15f);
        colori.pressedColor = Color.Lerp(coloreBase, Color.black, 0.20f);
        colori.selectedColor = coloreBase;
        colori.disabledColor = new Color(coloreBase.r, coloreBase.g, coloreBase.b, 0.35f);
        colori.colorMultiplier = 1f;
        colori.fadeDuration = 0.1f;
        btn.colors = colori;

        GameObject figlioTesto = NuovoOggettoUI("Testo", go.transform);
        ImpostaRectStretchCompleto(figlioTesto);
        var tmp = figlioTesto.AddComponent<TextMeshProUGUI>();
        tmp.text = "✕";
        tmp.fontSize = 28f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        return btn;
    }

    /// <summary>
    /// Crea il pannello di inserimento della chiave API Anthropic, mostrato da
    /// <see cref="ReceiptUIController"/> solo quando manca una chiave valida: etichetta, campo
    /// di testo (mascherato come password) e pulsante "Salva".
    /// </summary>
    private static (GameObject pannello, TMP_InputField campo, Button pulsanteSalva) CreaPannelloApiKey(Transform genitore)
    {
        GameObject pannello = NuovoOggettoUI("PannelloApiKey", genitore);
        AggiungiLayoutElement(pannello).preferredHeight = 56f;

        var bg = pannello.AddComponent<Image>();
        bg.color = new Color(0.30f, 0.22f, 0.06f, 0.85f);

        var layout = pannello.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 8, 8);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        GameObject etichettaGO = NuovoOggettoUI("Etichetta", pannello.transform);
        AggiungiLayoutElement(etichettaGO).preferredWidth = 280f;
        var etichetta = etichettaGO.AddComponent<TextMeshProUGUI>();
        etichetta.text = "Chiave API Anthropic mancante:";
        etichetta.fontSize = 20f;
        etichetta.alignment = TextAlignmentOptions.MidlineLeft;
        etichetta.color = new Color(1f, 0.85f, 0.55f);

        TMP_InputField campo = CreaCampoInputApiKey(pannello.transform);

        Button pulsanteSalva = CreaPulsante(
            pannello.transform, "PulsanteSalvaApiKey", "Salva", 40f, new Color(0.20f, 0.55f, 0.30f));
        pulsanteSalva.GetComponent<LayoutElement>().preferredWidth = 140f;

        return (pannello, campo, pulsanteSalva);
    }

    /// <summary>
    /// Crea un TMP_InputField con contenuto mascherato (password), completo della struttura
    /// interna richiesta da TextMeshPro (Text Area con Placeholder e Text).
    /// </summary>
    private static TMP_InputField CreaCampoInputApiKey(Transform genitore)
    {
        GameObject campoGO = NuovoOggettoUI("CampoApiKey", genitore);
        AggiungiLayoutElement(campoGO).flexibleWidth = 1f;

        var bg = campoGO.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.08f);

        var inputField = campoGO.AddComponent<TMP_InputField>();
        inputField.targetGraphic = bg;
        inputField.contentType = TMP_InputField.ContentType.Password;

        GameObject textArea = NuovoOggettoUI("Text Area", campoGO.transform);
        ImpostaRectStretchCompleto(textArea, margine: 8f);
        textArea.AddComponent<RectMask2D>();

        GameObject placeholderGO = NuovoOggettoUI("Placeholder", textArea.transform);
        ImpostaRectStretchCompleto(placeholderGO);
        var placeholder = placeholderGO.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Incolla qui la chiave API (sk-ant-...)";
        placeholder.fontSize = 18f;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.4f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        GameObject textoGO = NuovoOggettoUI("Text", textArea.transform);
        ImpostaRectStretchCompleto(textoGO);
        var testo = textoGO.AddComponent<TextMeshProUGUI>();
        testo.fontSize = 18f;
        testo.color = Color.white;
        testo.alignment = TextAlignmentOptions.MidlineLeft;

        inputField.textViewport = textArea.GetComponent<RectTransform>();
        inputField.textComponent = testo;
        inputField.placeholder = placeholder;
        inputField.text = string.Empty;

        return inputField;
    }

    private static TMP_Text CreaTestoInformativo(Transform genitore, string nome, string testo)
    {
        GameObject go = NuovoOggettoUI(nome, genitore);
        AggiungiLayoutElement(go).preferredHeight = 36f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = testo;
        tmp.fontSize = 20f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.75f, 0.75f, 0.75f);

        return tmp;
    }

    private static RectTransform CreaRigaContatori(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("RigaContatori", genitore);
        AggiungiLayoutElement(go).preferredHeight = 60f;

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SpaziaturaLayout;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        return go.GetComponent<RectTransform>();
    }

    private static TMP_Text CreaContatore(Transform genitore, string nome, string testo, Color colore)
    {
        GameObject go = NuovoOggettoUI(nome, genitore);

        var bg = go.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.06f);

        GameObject figlioTesto = NuovoOggettoUI("Testo", go.transform);
        ImpostaRectStretchCompleto(figlioTesto);
        var tmp = figlioTesto.AddComponent<TextMeshProUGUI>();
        tmp.text = testo;
        tmp.fontSize = 32f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = colore;

        return tmp;
    }

    /// <summary>
    /// Crea la riga con le tre scrollview separate (Aggiunti, Aggiornati, Invariati),
    /// posizionata subito sotto la riga dei contatori e allineata ad essa: ciascun pannello
    /// ha titolo e area di testo scorrevole indipendente, con la propria scrollbar.
    /// </summary>
    private static (TMP_Text aggiunti, TMP_Text aggiornati, TMP_Text invariati) CreaRigaListeProdotti(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("RigaListeProdotti", genitore);
        var layoutElement = AggiungiLayoutElement(go);
        layoutElement.flexibleHeight = 1f;
        layoutElement.minHeight = 220f;

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SpaziaturaLayout;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        // Titoli in solo testo (senza emoji/simboli): il font SDF di default di TextMeshPro
        // non include i glifi per caratteri come ➕ 🔄 ➖, che verrebbero renderizzati come
        // quadratini "mancante glifo" (☐) invece del simbolo previsto.
        TMP_Text testoAggiunti = CreaColonnaConTitolo(
            go.transform, "ColonnaListaAggiunti", "Aggiunti", new Color(0.30f, 0.80f, 0.40f));
        TMP_Text testoAggiornati = CreaColonnaConTitolo(
            go.transform, "ColonnaListaAggiornati", "Aggiornati", new Color(0.95f, 0.75f, 0.25f));
        TMP_Text testoInvariati = CreaColonnaConTitolo(
            go.transform, "ColonnaListaInvariati", "Invariati", new Color(0.70f, 0.70f, 0.70f));

        return (testoAggiunti, testoAggiornati, testoInvariati);
    }

    private static GameObject CreaIndicatoreCaricamento(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("IndicatoreCaricamento", genitore);
        AggiungiLayoutElement(go).preferredHeight = 34f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Elaborazione in corso...";
        tmp.fontSize = 24f;
        tmp.fontStyle = FontStyles.Italic;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.80f, 0.30f);

        go.SetActive(false);

        return go;
    }

    /// <summary>
    /// Crea la riga inferiore che ospita la colonna del log delle operazioni. Si spartisce lo
    /// spazio verticale residuo del layout con <see cref="CreaRigaListeProdotti"/> (entrambe
    /// hanno flexibleHeight 1, quindi lo spazio libero viene diviso equamente tra le due).
    /// </summary>
    private static RectTransform CreaRigaInferiore(Transform genitore)
    {
        GameObject go = NuovoOggettoUI("RigaInferiore", genitore);
        var layoutElement = AggiungiLayoutElement(go);
        layoutElement.flexibleHeight = 1f;
        layoutElement.minHeight = 180f;

        var layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = SpaziaturaLayout;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        return go.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Crea una colonna con un titolo in alto e, sotto, un'area di testo scorrevole che
    /// occupa lo spazio residuo. Usata sia per il log sia per le tre liste prodotti
    /// (Aggiunti/Aggiornati/Invariati), ciascuna con la propria scrollbar indipendente.
    /// </summary>
    private static TMP_Text CreaColonnaConTitolo(Transform genitore, string nomeContenitore, string titolo, Color coloreTesto)
    {
        GameObject colonna = NuovoOggettoUI(nomeContenitore, genitore);
        AggiungiLayoutElement(colonna).flexibleWidth = 1f;

        var layout = colonna.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        GameObject titoloGO = NuovoOggettoUI("Titolo", colonna.transform);
        AggiungiLayoutElement(titoloGO).preferredHeight = 30f;
        var tmpTitolo = titoloGO.AddComponent<TextMeshProUGUI>();
        tmpTitolo.text = titolo;
        tmpTitolo.fontSize = 24f;
        tmpTitolo.fontStyle = FontStyles.Bold;
        tmpTitolo.alignment = TextAlignmentOptions.Left;
        tmpTitolo.color = new Color(0.85f, 0.85f, 0.85f);

        return CreaAreaTestoScorrevole(colonna.transform, nomeContenitore + "ScrollView", coloreTesto);
    }

    /// <summary>
    /// Crea un'area di testo scorrevole generica (ScrollRect + Viewport con RectMask2D +
    /// Content con auto-sizing), riutilizzata sia per il log sia per il dettaglio prodotti.
    /// </summary>
    private static TMP_Text CreaAreaTestoScorrevole(Transform genitore, string nomeOggetto, Color coloreTesto)
    {
        GameObject scrollGO = NuovoOggettoUI(nomeOggetto, genitore);
        var layoutElement = AggiungiLayoutElement(scrollGO);
        layoutElement.flexibleHeight = 1f;
        layoutElement.minHeight = 200f;

        var bg = scrollGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.45f);

        var scrollRect = scrollGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        GameObject viewportGO = NuovoOggettoUI("Viewport", scrollGO.transform);
        ImpostaRectStretchCompleto(viewportGO, margine: 4f);
        viewportGO.AddComponent<RectMask2D>();

        GameObject contentGO = NuovoOggettoUI("Content", viewportGO.transform);
        var contentRect = contentGO.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        var contentLayout = contentGO.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(12, 12, 8, 8);
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        var contentFitter = contentGO.AddComponent<ContentSizeFitter>();
        contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject testoGO = NuovoOggettoUI("Testo", contentGO.transform);
        var tmp = testoGO.AddComponent<TextMeshProUGUI>();
        tmp.text = string.Empty;
        tmp.fontSize = 18f;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = coloreTesto;

        scrollRect.viewport = viewportGO.GetComponent<RectTransform>();
        scrollRect.content = contentRect;

        return tmp;
    }

    // ------------------------------------------------------------------
    // Collegamento dei riferimenti sul componente ReceiptUIController
    // ------------------------------------------------------------------

    private static void CollegaRiferimenti(
        ReceiptUIController controller,
        AnthropicService servizioAnthropic,
        ZipAndDatabaseManager gestoreDatabase,
        Button pulsanteSelezionaScontrino,
        Button pulsanteSelezionaBackupZip,
        Button pulsanteElaboraConAI,
        Button pulsanteSalvaModificheZip,
        Button pulsanteReset,
        GameObject pannelloApiKey,
        TMP_InputField campoApiKey,
        Button pulsanteSalvaApiKey,
        TMP_Text testoAggiunti,
        TMP_Text testoAggiornati,
        TMP_Text testoInvariati,
        TMP_Text testoListaAggiunti,
        TMP_Text testoListaAggiornati,
        TMP_Text testoListaInvariati,
        TMP_Text testoLog,
        TMP_Text testoPercorsoBackupZip,
        RawImage anteprima,
        GameObject indicatoreCaricamento,
        CanvasGroup canvasGroupPrincipale)
    {
        var serializedObject = new SerializedObject(controller);

        ImpostaRiferimento(serializedObject, "servizioAnthropic", servizioAnthropic);
        ImpostaRiferimento(serializedObject, "gestoreDatabase", gestoreDatabase);
        ImpostaRiferimento(serializedObject, "pulsanteSelezionaScontrino", pulsanteSelezionaScontrino);
        ImpostaRiferimento(serializedObject, "pulsanteSelezionaBackupZip", pulsanteSelezionaBackupZip);
        ImpostaRiferimento(serializedObject, "pulsanteElaboraConAI", pulsanteElaboraConAI);
        ImpostaRiferimento(serializedObject, "pulsanteSalvaModificheZip", pulsanteSalvaModificheZip);
        ImpostaRiferimento(serializedObject, "pulsanteReset", pulsanteReset);
        ImpostaRiferimento(serializedObject, "pannelloApiKey", pannelloApiKey);
        ImpostaRiferimento(serializedObject, "campoApiKey", campoApiKey);
        ImpostaRiferimento(serializedObject, "pulsanteSalvaApiKey", pulsanteSalvaApiKey);
        ImpostaRiferimento(serializedObject, "testoContatoreAggiunti", testoAggiunti);
        ImpostaRiferimento(serializedObject, "testoContatoreAggiornati", testoAggiornati);
        ImpostaRiferimento(serializedObject, "testoContatoreInvariati", testoInvariati);
        ImpostaRiferimento(serializedObject, "testoListaAggiunti", testoListaAggiunti);
        ImpostaRiferimento(serializedObject, "testoListaAggiornati", testoListaAggiornati);
        ImpostaRiferimento(serializedObject, "testoListaInvariati", testoListaInvariati);
        ImpostaRiferimento(serializedObject, "testoLog", testoLog);
        ImpostaRiferimento(serializedObject, "testoPercorsoBackupZip", testoPercorsoBackupZip);
        ImpostaRiferimento(serializedObject, "anteprimaScontrino", anteprima);
        ImpostaRiferimento(serializedObject, "indicatoreCaricamento", indicatoreCaricamento);
        ImpostaRiferimento(serializedObject, "canvasGroupPrincipale", canvasGroupPrincipale);

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Collega il pulsante di chiusura al componente <see cref="GestoreApplicazione"/>.</summary>
    private static void CollegaRiferimentoPulsanteChiudi(GestoreApplicazione gestoreApplicazione, Button pulsanteChiudi)
    {
        var serializedObject = new SerializedObject(gestoreApplicazione);
        ImpostaRiferimento(serializedObject, "pulsanteChiudi", pulsanteChiudi);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ImpostaRiferimento(SerializedObject serializedObject, string nomeCampo, Object valore)
    {
        SerializedProperty proprieta = serializedObject.FindProperty(nomeCampo);
        if (proprieta == null)
        {
            Debug.LogWarning(
                $"[UISceneBuilder] Campo '{nomeCampo}' non trovato su ReceiptUIController: " +
                "verificare che il nome del campo nello script corrisponda.");
            return;
        }
        proprieta.objectReferenceValue = valore;
    }

    // ------------------------------------------------------------------
    // Helper generici per la costruzione della UI
    // ------------------------------------------------------------------

    private static GameObject NuovoOggettoUI(string nome, Transform genitore)
    {
        GameObject go = new GameObject(nome, typeof(RectTransform));
        go.transform.SetParent(genitore, false);
        return go;
    }

    /// <summary>Imposta un RectTransform in modalità "stretch" completo rispetto al genitore, con un margine opzionale uniforme.</summary>
    private static RectTransform ImpostaRectStretchCompleto(GameObject go, float margine = 0f)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(margine, margine);
        rt.offsetMax = new Vector2(-margine, -margine);
        return rt;
    }

    private static LayoutElement AggiungiLayoutElement(GameObject go)
    {
        return go.AddComponent<LayoutElement>();
    }
}
#endif
