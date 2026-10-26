# Correttore IT 1.3 — correttore e assistente di scrittura con AI locale (portatile)

Corregge l'italiano, completa le frasi mentre scrivi e scrive le risposte ai messaggi, **in qualsiasi
programma di Windows e in Chrome**. L'AI gira sul tuo PC con [llama.cpp](https://github.com/ggml-org/llama.cpp):
nessun account, nessun abbonamento, **nessun testo inviato su Internet**.

**Programma portatile:** vive tutto nella sua cartella (programma, motore, modelli, impostazioni).
Non si installa e non scrive nel registro di Windows: si copia su una chiavetta o su un altro PC e funziona.

☕ Offrimi un caffè: https://buymeacoffee.com/bitnbit · YouTube: https://www.youtube.com/@bitnbit · Sito: https://bitnbit.com

*Local, private Italian proofreader and writing assistant for Windows and Chrome (llama.cpp, Qwen3.5).
Portable: everything lives in one folder. Fixes spelling and grammar, suggests the next words while you type
(Tab to accept), writes replies, rewrites, translates. Interface in Italian; writing in 7 languages.*

## Cosa fa
| | |
|---|---|
| **Correzione** | Seleziona un testo in qualsiasi programma (Word, Blocco note, WhatsApp, email…) e premi **Ctrl+Maiusc+Spazio**: corretto sul posto. Dizionario di Windows + AI: accenti, apostrofi, e/è, a/ha, concordanze. Nomi propri intatti. |
| **Testo grigio** | In Chrome il seguito della frase compare in grigio: **Tab** lo accetta. Compare solo quando il modello è sicuro, ignora menu e pulsanti della pagina, non inventa orari o prezzi. |
| **Risposte** | Pulsante viola in Chrome o pulsante rotondo in Windows: scrive la risposta al messaggio, seguendo la tua bozza se l'hai iniziata. |
| **Riscritture** | Completa la frase, più formale, più semplice e breve, traduci. |
| **Lingue** | Italiano, inglese, francese, spagnolo, tedesco, portoghese, rumeno. |

## Novità della 1.3
- **Testo grigio molto più pertinente.** Nuovo modello *Qwen3.5 2B Base*, che continua il testo come una
  persona invece di fare l'assistente. Il programma guarda la sicurezza del modello parola per parola e mostra
  solo la parte certa: meglio nessun suggerimento che uno a caso.
  Esempi: «scusa il → *ritardo.*», «Spero che tu stia → *bene.*», «purtroppo domani → *non riesco a passare in ufficio.*»
- **Il contesto della pagina conta solo se è un vero messaggio** (una mail, una chat): prima i menu del sito
  ("Home Notizie Meteo…") finivano nei suggerimenti.
- **Più leggero:** circa 1 GB di RAM in meno, motore ripulito dagli strumenti inutili.
- **Portatile:** niente più installazione in AppData; la cartella si sposta o si copia così com'è.

## Download e avvio
1. Scarica `CorrettoreIT-portatile-v1.3.0.zip` dalla pagina **[Releases](https://github.com/BITNBIT/correttore-it/releases/latest)** ed estrailo dove vuoi.
2. Doppio clic su **Prepara.bat** (solo la prima volta). Riconosce il PC e:
   - sceglie il motore: **NVIDIA** (CUDA), **AMD Radeon / Intel Arc** (Vulkan) oppure **processore**;
   - scarica i modelli AI da HuggingFace (circa 4 GB, una volta sola; riprende da dove si ferma);
   - crea il programma con il .NET già presente in Windows.
3. Da lì in poi basta **CorrettoreIT.exe**. Avvio con Windows: tasto destro sull'icona vicino all'orologio.
4. Chrome o Edge: `chrome://extensions` → *Modalità sviluppatore* → *Carica estensione non pacchettizzata* →
   cartella `estensione-chrome`.

**Requisiti:** Windows 10 o 11 (64 bit). Consigliati 12 GB di RAM oppure una scheda video con 4 GB;
con meno memoria usa un modello più piccolo (Qwen3.5 2B).

## Modelli
| Modello | Uso | Peso |
|---|---|---|
| [Qwen3.5 4B MTP](https://huggingface.co/unsloth/Qwen3.5-4B-MTP-GGUF) | correzione, risposte, riscritture, traduzioni | 2,8 GB |
| [Qwen3.5 2B Base](https://huggingface.co/mradermacher/Qwen3.5-2B-Base-GGUF) | testo grigio mentre scrivi | 1,3 GB |
| [Qwen3.5 2B](https://huggingface.co/unsloth/Qwen3.5-2B-GGUF) | tutto, sui PC con poca memoria | 1,3 GB |

## Privacy e leggerezza
- Il testo non lascia mai il PC: il motore ascolta solo su `127.0.0.1`.
- Nessun file di log, nessuna cache che cresce su disco.
- Motore a priorità bassa; pause automatiche sui testi lunghi; protezione temperatura per le schede NVIDIA.
- Disattivato dall'icona = motore spento, 0 RAM.

## Struttura
```
app/CorrettoreIT.cs     programma (C#, .NET Framework 4 incluso in Windows)
estensione-chrome/      estensione per Chrome / Edge
prepara.ps1, Prepara.bat preparazione della cartella su un PC nuovo
Elimina.bat             chiude il programma e cancella la cartella
```

## Licenza
MIT © BITNBIT. llama.cpp: MIT. Modelli Qwen3.5: Apache 2.0.
