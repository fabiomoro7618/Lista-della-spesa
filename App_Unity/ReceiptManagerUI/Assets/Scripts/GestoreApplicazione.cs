using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Gestisce la finestra dell'applicazione e la sua chiusura nella build Standalone Windows:
/// imposta l'avvio in modalità finestra ridimensionabile (anziché schermo intero forzato) e
/// permette di uscire dall'app tramite il pulsante di chiusura in UI o il tasto Esc.
/// </summary>
public class GestoreApplicazione : MonoBehaviour
{
    [Header("Finestra (solo build Standalone)")]
    [SerializeField] private int larghezzaFinestra = 1280;
    [SerializeField] private int altezzaFinestra = 720;

    [Header("Pulsanti")]
    [SerializeField] private UnityEngine.UI.Button pulsanteChiudi;

    private void Awake()
    {
        if (pulsanteChiudi != null)
        {
            pulsanteChiudi.onClick.AddListener(Esci);
        }
    }

    private void Start()
    {
        // In Editor la finestra è gestita dal Game View: la resize ha effetto solo nella
        // build Standalone, dove forza la modalità finestra (con barra del titolo) invece
        // dello schermo intero, lasciando comunque la finestra ridimensionabile dall'utente
        // (impostazione "Resizable Window" nei Player Settings).
#if !UNITY_EDITOR
        Screen.SetResolution(larghezzaFinestra, altezzaFinestra, FullScreenMode.Windowed);
#endif
    }

    private void OnDestroy()
    {
        if (pulsanteChiudi != null)
        {
            pulsanteChiudi.onClick.RemoveListener(Esci);
        }
    }

    /// <summary>
    /// Intercetta il tasto Esc per chiudere l'applicazione. Il progetto usa il nuovo Input
    /// System (Project Settings > Active Input Handling: "Input System Package (New)"), che
    /// disabilita del tutto la classe legacy UnityEngine.Input: chiamare Input.GetKeyDown in
    /// questa configurazione lancia un'InvalidOperationException a runtime in Play Mode.
    /// </summary>
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Esci();
        }
#else
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Esci();
        }
#endif
    }

    /// <summary>Chiude l'applicazione (ferma il Play Mode in Editor, esce dalla build).</summary>
    public void Esci()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
