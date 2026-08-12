using UnityEngine;

/// <summary>
/// Centralizza il salvataggio e il caricamento persistente (tramite PlayerPrefs, che sulla
/// build Standalone Windows scrive nel registro sotto HKCU\Software\[Company]\[Product]) della
/// chiave API Anthropic e dell'ultimo percorso del backup ZIP selezionato dall'utente, così da
/// non doverli reinserire ad ogni avvio dell'eseguibile.
/// </summary>
public static class GestoreConfigurazione
{
    private const string ChiavePrefApiKey = "ReceiptManager.AnthropicApiKey";
    private const string ChiavePrefUltimoZip = "ReceiptManager.UltimoPercorsoZip";

    /// <summary>Carica la chiave API Anthropic salvata, oppure stringa vuota se non presente.</summary>
    public static string CaricaApiKey()
    {
        return PlayerPrefs.GetString(ChiavePrefApiKey, string.Empty);
    }

    /// <summary>Salva persistentemente la chiave API Anthropic.</summary>
    public static void SalvaApiKey(string chiave)
    {
        PlayerPrefs.SetString(ChiavePrefApiKey, chiave ?? string.Empty);
        PlayerPrefs.Save();
    }

    /// <summary>Carica l'ultimo percorso del backup ZIP selezionato, oppure stringa vuota se non presente.</summary>
    public static string CaricaUltimoPercorsoZip()
    {
        return PlayerPrefs.GetString(ChiavePrefUltimoZip, string.Empty);
    }

    /// <summary>Salva persistentemente l'ultimo percorso del backup ZIP selezionato dall'utente.</summary>
    public static void SalvaUltimoPercorsoZip(string percorsoCompleto)
    {
        PlayerPrefs.SetString(ChiavePrefUltimoZip, percorsoCompleto ?? string.Empty);
        PlayerPrefs.Save();
    }
}
