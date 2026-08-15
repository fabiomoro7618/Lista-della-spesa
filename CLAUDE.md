# CLAUDE.md

Istruzioni per Claude Code in questo repository.

## Lingua

Tutte le risposte, i riepiloghi e i commenti devono essere rigorosamente in lingua italiana. Questo vale anche per i commenti nel codice, i messaggi di commit e qualsiasi testo generato durante il lavoro su questo repository.

## Struttura del progetto

- `App_Python/` — API/backend Python (`main.py`, test in `test_main.py`)
- `App_AndroFlutter/` — app mobile Flutter, collegata alle API Python
- `App_Unity/` — progetto Unity (ReceiptManagerUI)

## Autonomia operativa

Per qualsiasi modifica ai file, esecuzione di test o comandi di build, procedi in autonomia senza chiedere conferma preventiva. Non fermarti per chiedere il permesso di:

- modificare, creare o eliminare file di codice
- eseguire test (es. `pytest`, `flutter test`)
- eseguire build o comandi di compilazione (es. `flutter build`, `flutter pub get`)
- eseguire refactoring o correggere bug individuati durante il lavoro

Fermati e chiedi chiarimenti solo quando hai dubbi funzionali sull'architettura o sui requisiti dell'app (es. comportamento atteso di una feature, scelte di design non specificate, ambiguità nei requisiti).
