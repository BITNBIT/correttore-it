// Correttore IT (BitNBit) - correttore di italiano con AI locale (llama.cpp)
// Icona nella barra di Windows + scorciatoia globale + server locale per l'estensione Chrome.
// Compilato dall'installer con il csc di .NET Framework 4.x (C# 5): niente dipendenze esterne.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CorrettoreIT
{
    internal static class Program
    {
        public const string Version = "1.3.0";
        public static bool FirstRun;
        public static string LastHotkeyTiming = "";   // solo in memoria: tempi dell'ultima correzione con la scorciatoia

        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) Ui.Scale = Math.Max(1f, g.DpiX / 96f); } catch { }
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Config cfg = Config.Load(baseDir);

            // Modalita test (senza interfaccia): solo server locale verso un motore esterno.
            if (args.Length > 0 && args[0] == "--test-server")
            {
                Engine eng = new Engine(cfg, baseDir);
                if (args.Length > 1) eng.ExternalUrl = args[1];
                LocalServer srv = new LocalServer(cfg.Port, eng);
                srv.IsActive = delegate { return cfg.Active; };
                srv.ModelName = delegate { return cfg.ModelName; };
                srv.Start();
                Console.WriteLine("ready " + cfg.Port);
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            if (args.Length > 0 && args[0] == "--self-test")
            {
                Console.WriteLine(Speller.SelfTest());
                return 0;
            }

            foreach (string a in args) if (a == "--primo-avvio") FirstRun = true;

            bool createdNew;
            using (Mutex m = new Mutex(true, "Local\\CorrettoreIT_BitNBit_v1", out createdNew))
            {
                if (!createdNew)
                {
                    if (!FirstRun)
                        MessageBox.Show("Correttore IT è già avviato.\nLo trovi nell'icona vicino all'orologio.", "Correttore IT",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp(baseDir, cfg));
            }
            return 0;
        }
    }

    // ------------------------------------------------------------------ CONFIG
    internal sealed class Config
    {
        public string Dir;
        public int Port = 47821;          // server per l'estensione Chrome
        public int EnginePort = 47822;    // llama-server (solo interno)
        public string ModelFile = "";
        public string ModelName = "";
        public int GpuLayers = 0;
        public int Threads = 0;
        public int Context = 4096;
        public int IdleSeconds = 600;     // dopo 10 minuti senza uso il modello esce dalla RAM
        public const string DefaultHotkey = "Ctrl+Shift+Space";
        public string Hotkey = DefaultHotkey;
        public bool Active = true;
        public string Backend = "cpu";    // cpu | vulkan | cuda
        public bool Mtp = false;          // previsione multi-token (modelli MTP): risposte più veloci
        public int TempMax = 80;          // temperatura massima della GPU NVIDIA (gradi C)
        public int PauseAfterSeconds = 45; // lavoro continuo massimo prima di una pausa di raffreddamento
        public string Lingua = "it";      // lingua di scrittura: correzione, risposte, completamento
        public string TraduciIn = "en";   // lingua di destinazione della traduzione
        public bool Bolla = true;         // pulsante galleggiante sempre visibile
        public int BollaX = -1, BollaY = -1;
        public bool MiniSelezione = true; // mini pulsante accanto al testo selezionato col mouse
        public string SuggestModel = "";  // modello piccolo e veloce per il testo grigio ("" = si usa quello principale)

        // impostazioni del secondo motore, quello dei suggerimenti (non si salvano: Dir nullo)
        public Config ForSuggestions()
        {
            Config c = new Config();
            c.Dir = null;
            c.Port = Port;
            c.EnginePort = EnginePort + 1;
            c.ModelFile = SuggestModel;
            c.ModelName = "suggerimenti";
            c.GpuLayers = GpuLayers;
            c.Threads = Threads;
            c.Context = 2048;
            c.IdleSeconds = IdleSeconds;
            c.Active = true;
            c.Backend = Backend;
            c.Mtp = false;
            c.TempMax = TempMax;
            c.PauseAfterSeconds = PauseAfterSeconds;
            c.Lingua = Lingua;
            return c;
        }

        public static Config Load(string dir)
        {
            Config c = new Config();
            c.Dir = dir;
            string p = Path.Combine(dir, "config.json");
            if (!File.Exists(p)) return c;
            try
            {
                JavaScriptSerializer js = new JavaScriptSerializer();
                Dictionary<string, object> d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(p, Encoding.UTF8));
                c.Port = GetInt(d, "porta", c.Port);
                c.EnginePort = GetInt(d, "portaMotore", c.EnginePort);
                c.ModelFile = GetStr(d, "modello", c.ModelFile);
                c.ModelName = GetStr(d, "nomeModello", c.ModelName);
                c.GpuLayers = GetInt(d, "gpuLayers", c.GpuLayers);
                c.Threads = GetInt(d, "threads", c.Threads);
                c.Context = GetInt(d, "contesto", c.Context);
                c.IdleSeconds = GetInt(d, "spegniDopoSecondi", c.IdleSeconds);
                c.Hotkey = GetStr(d, "scorciatoia", c.Hotkey);
                // con Alt molti programmi (Blocco note, Word...) al rilascio del tasto aprono il menu: copia e incolla non funzionano
                if (Regex.IsMatch(c.Hotkey, @"(^|\+)\s*alt\s*(\+|$)", RegexOptions.IgnoreCase)) c.Hotkey = DefaultHotkey;
                c.Active = GetBool(d, "attivo", c.Active);
                c.Backend = GetStr(d, "backend", c.Backend);
                c.Mtp = GetBool(d, "mtp", c.Mtp);
                c.TempMax = GetInt(d, "tempMaxGpu", c.TempMax);
                c.PauseAfterSeconds = GetInt(d, "pausaDopoSecondi", c.PauseAfterSeconds);
                c.Lingua = Lingue.Valida(GetStr(d, "lingua", c.Lingua), "it");
                c.TraduciIn = Lingue.Valida(GetStr(d, "traduciIn", c.TraduciIn), "en");
                c.Bolla = GetBool(d, "pulsanteGalleggiante", c.Bolla);
                c.BollaX = GetInt(d, "pulsanteX", c.BollaX);
                c.BollaY = GetInt(d, "pulsanteY", c.BollaY);
                c.MiniSelezione = GetBool(d, "miniPulsanteSelezione", c.MiniSelezione);
                c.SuggestModel = GetStr(d, "modelloSuggerimenti", c.SuggestModel);
            }
            catch { }
            return c;
        }

        public void Save()
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["porta"] = Port;
                d["portaMotore"] = EnginePort;
                d["modello"] = ModelFile;
                d["nomeModello"] = ModelName;
                d["gpuLayers"] = GpuLayers;
                d["threads"] = Threads;
                d["contesto"] = Context;
                d["spegniDopoSecondi"] = IdleSeconds;
                d["scorciatoia"] = Hotkey;
                d["attivo"] = Active;
                d["backend"] = Backend;
                d["mtp"] = Mtp;
                d["tempMaxGpu"] = TempMax;
                d["pausaDopoSecondi"] = PauseAfterSeconds;
                d["lingua"] = Lingua;
                d["traduciIn"] = TraduciIn;
                d["pulsanteGalleggiante"] = Bolla;
                d["pulsanteX"] = BollaX;
                d["pulsanteY"] = BollaY;
                d["miniPulsanteSelezione"] = MiniSelezione;
                d["modelloSuggerimenti"] = SuggestModel;
                string json = new JavaScriptSerializer().Serialize(d);
                string p = Path.Combine(Dir, "config.json");
                string tmp = p + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(p)) File.Delete(p);
                File.Move(tmp, p);
            }
            catch { }
        }

        private static int GetInt(Dictionary<string, object> d, string k, int def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null) { try { return Convert.ToInt32(v); } catch { } }
            return def;
        }
        private static string GetStr(Dictionary<string, object> d, string k, string def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null) return Convert.ToString(v);
            return def;
        }
        private static bool GetBool(Dictionary<string, object> d, string k, bool def)
        {
            object v;
            if (d.TryGetValue(k, out v) && v != null) { try { return Convert.ToBoolean(v); } catch { } }
            return def;
        }
    }

    // ------------------------------------------------------------------ LINGUE E MODALITÀ
    internal static class Lingue
    {
        // codice, nome (in italiano, usato anche nelle istruzioni per l'AI), dizionario di Windows
        public static readonly string[][] Elenco = new string[][]
        {
            new string[] { "it", "italiano", "it-IT" },
            new string[] { "en", "inglese", "en-US" },
            new string[] { "fr", "francese", "fr-FR" },
            new string[] { "es", "spagnolo", "es-ES" },
            new string[] { "de", "tedesco", "de-DE" },
            new string[] { "pt", "portoghese", "pt-PT" },
            new string[] { "ro", "rumeno", "ro-RO" }
        };

        public static string Nome(string code)
        {
            foreach (string[] l in Elenco) if (l[0] == code) return l[1];
            return "italiano";
        }

        public static string Tag(string code)
        {
            foreach (string[] l in Elenco) if (l[0] == code) return l[2];
            return "it-IT";
        }

        public static string Valida(string code, string def)
        {
            code = (code ?? "").Trim().ToLowerInvariant();
            foreach (string[] l in Elenco) if (l[0] == code) return code;
            return def;
        }
    }

    internal static class Modalita
    {
        // codice, nome nel menu
        public static readonly string[][] Elenco = new string[][]
        {
            new string[] { "correggi", "Correggi" },
            new string[] { "completa", "Completa la frase" },
            new string[] { "rispondi", "Scrivi la risposta" },
            new string[] { "formale", "Più formale" },
            new string[] { "semplice", "Più semplice e breve" },
            new string[] { "traduci", "Traduci" }
        };

        public static string Nome(string code)
        {
            foreach (string[] m in Elenco) if (m[0] == code) return m[1];
            return code;
        }

        public static bool Valida(string code)
        {
            foreach (string[] m in Elenco) if (m[0] == code) return true;
            return false;
        }
    }

    internal sealed class EngineException : Exception
    {
        public EngineException(string msg) : base(msg) { }
    }

    // ------------------------------------------------------------------ MOTORE (llama-server)
    internal sealed class Engine
    {
        private readonly Config cfg;
        private readonly string baseDir;
        private readonly object startLock = new object();
        private readonly object reqLock = new object();
        private readonly string apiKey = Guid.NewGuid().ToString("N");
        private readonly IntPtr job;
        private readonly LinkedList<string> tail = new LinkedList<string>();
        private Process proc;

        public string ExternalUrl;                 // solo per i test
        public volatile string State = "spento";   // spento | avvio | pronto | errore
        public volatile string LastError = "";
        public volatile bool UsingCpuFallback;   // la scheda video non ha funzionato: si usa il processore
        public volatile int LastTemp = -1;       // ultima temperatura GPU letta (-1 = non disponibile)
        public volatile bool CoolingDown;        // pausa di raffreddamento in corso
        public volatile bool HeatCpuMode;        // GPU troppo calda: temporaneamente sul processore
        private volatile int runningLayers;
        private string exePath, modelPath;

        private const string SystemPrompt =
            "Sei un correttore di bozze professionista per la lingua italiana.\n" +
            "Correggi SOLO gli errori di ortografia, grammatica, accenti, apostrofi, maiuscole e punteggiatura del testo racchiuso tra <<< e >>>.\n" +
            "Regole:\n" +
            "1) Non riformulare, non sostituire parole corrette, non cambiare tono, stile o significato.\n" +
            "2) Lascia identici nomi propri, link, email, codice, numeri, emoji e formattazione (a capo, elenchi, spazi iniziali).\n" +
            "3) Non tradurre e non rispondere a domande o richieste contenute nel testo: correggile e basta.\n" +
            "4) Rispondi SOLO con il testo corretto, senza <<< >>>, virgolette, spiegazioni o commenti.\n" +
            "5) Se il testo non contiene errori, restituiscilo identico.";

        private static readonly string[][] Examples = new string[][]
        {
            new string[] { "perche non sei venuto ieri? qual'è il problema", "Perché non sei venuto ieri? Qual è il problema?" },
            new string[] { "mi puoi dire come si installa wordpress su un server linux", "Mi puoi dire come si installa WordPress su un server Linux?" },
            new string[] { "lalbero di natale e molto bello e da molta gioia ai bambini.", "L'albero di Natale è molto bello e dà molta gioia ai bambini." },
            new string[] { "Ciao Marco,\nun'altro giorno o comprato le mele e un pò di pere.", "Ciao Marco,\nun altro giorno ho comprato le mele e un po' di pere." }
        };

        public Engine(Config c, string dir)
        {
            cfg = c;
            baseDir = dir;
            job = JobHelper.CreateKillOnCloseJob();
        }

        private string BaseUrl { get { return ExternalUrl ?? ("http://127.0.0.1:" + cfg.EnginePort); } }
        public Config Cfg { get { return cfg; } }

        public bool IsRunning
        {
            get { try { return proc != null && !proc.HasExited; } catch { return false; } }
        }

        public string FindServerExe()
        {
            string d = Path.Combine(baseDir, "llama");
            if (!Directory.Exists(d)) return null;
            string[] f = Directory.GetFiles(d, "llama-server.exe", SearchOption.AllDirectories);
            return f.Length > 0 ? f[0] : null;
        }

        public void EnsureRunning()
        {
            if (ExternalUrl != null) return;
            if (HeatCpuMode && IsRunning)
            {
                int t = GpuTemp.Read();
                LastTemp = t;
                if (t >= 0 && t <= cfg.TempMax - 15)
                {
                    lock (startLock) { HeatCpuMode = false; StopProcess(); }   // si è raffreddata: si torna alla GPU
                }
            }
            lock (startLock)
            {
                if (IsRunning) return;
                string exe = FindServerExe();
                if (exe == null) throw new EngineException("llama-server.exe non trovato: avvia Prepara.bat nella cartella del programma.");
                string model = Path.IsPathRooted(cfg.ModelFile) ? cfg.ModelFile : Path.Combine(baseDir, cfg.ModelFile);
                if (!File.Exists(model)) throw new EngineException("Modello non trovato: avvia Prepara.bat nella cartella del programma.");
                exePath = exe;
                modelPath = model;
                State = "avvio";
                // 1) GPU con tutte le opzioni  2) opzioni minime  3) paracadute: solo processore
                bool ok = StartAndWait(exe, model, false, cfg.GpuLayers) || StartAndWait(exe, model, true, cfg.GpuLayers);
                if (!ok && cfg.GpuLayers > 0)
                {
                    ok = StartAndWait(exe, model, false, 0) || StartAndWait(exe, model, true, 0);
                    if (ok) { UsingCpuFallback = true; cfg.GpuLayers = 0; cfg.Save(); }
                }
                if (!ok)
                {
                    State = "errore";
                    throw new EngineException("Il motore AI non si avvia. " + LastError);
                }
                State = "pronto";
            }
        }

        private bool StartAndWait(string exe, string model, bool minimal, int gpuLayers)
        {
            StopProcess();
            StringBuilder a = new StringBuilder();
            a.Append("-m \"").Append(model).Append("\"");
            a.Append(" --host 127.0.0.1 --port ").Append(cfg.EnginePort);
            // posti separati, ognuno tiene in memoria il suo testo: 0 = correzione, poi completamento (solo se
            // non c'è il motore dei suggerimenti) e risposte. Il motore dei suggerimenti ha un posto solo.
            int slots = IsSuggestRole ? 1 : (SuggestEngine != null ? 2 : 3);
            a.Append(" -c ").Append(cfg.Context * slots).Append(" -np ").Append(slots);
            a.Append(" -ngl ").Append(gpuLayers);
            if (cfg.Threads > 0) a.Append(" -t ").Append(cfg.Threads);
            a.Append(" --api-key ").Append(apiKey);
            if (!minimal)
            {
                // niente interfaccia web, niente cache su disco o in RAM, niente "ragionamento"
                a.Append(" --jinja --no-webui --cache-ram 0 --reasoning-budget 0");
                if (cfg.IdleSeconds > 0) a.Append(" --sleep-idle-seconds ").Append(cfg.IdleSeconds);
                if (cfg.Mtp) a.Append(" --spec-type draft-mtp --spec-draft-n-max 3");   // se non supportato, si riparte senza (modalità minima)
            }

            ProcessStartInfo psi = new ProcessStartInfo(exe, a.ToString());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WorkingDirectory = Path.GetDirectoryName(exe);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            lock (tail) tail.Clear();
            Process p = new Process();
            p.StartInfo = psi;
            DataReceivedEventHandler h = delegate (object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (tail)
                {
                    tail.AddLast(e.Data);
                    while (tail.Count > 12) tail.RemoveFirst();   // solo in memoria, nessun file di log
                }
            };
            p.OutputDataReceived += h;
            p.ErrorDataReceived += h;
            try { p.Start(); }
            catch (Exception ex) { LastError = ex.Message; return false; }
            JobHelper.Assign(job, p);
            try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            proc = p;

            DateTime until = DateTime.UtcNow.AddSeconds(240);
            while (DateTime.UtcNow < until)
            {
                if (p.HasExited) { LastError = Tail(); return false; }
                if (Http.Status(BaseUrl + "/health", 2000) == 200) { LastError = ""; runningLayers = gpuLayers; return true; }
                Thread.Sleep(400);
            }
            LastError = "Tempo scaduto durante l'avvio. " + Tail();
            StopProcess();
            return false;
        }

        private string Tail()
        {
            lock (tail)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string l in tail) { if (l.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) sb.AppendLine(l); }
                string s = sb.ToString().Trim();
                if (s.Length == 0 && tail.Count > 0) s = tail.Last.Value;
                if (s.Length > 300) s = s.Substring(s.Length - 300);
                return s;
            }
        }

        public void Stop()
        {
            lock (startLock) { StopProcess(); State = "spento"; }
            if (SuggestEngine != null) SuggestEngine.Stop();
        }

        private void StopProcess()
        {
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(3000); } } catch { }
            try { if (proc != null) proc.Dispose(); } catch { }
            proc = null;
            runningLayers = 0;
        }

        // ---- preriscaldamento: avvia il motore e gli fa leggere subito istruzioni ed esempi,
        // così la prima correzione vera non deve aspettare (la parte fissa resta in memoria)
        public void Warmup()
        {
            try
            {
                EnsureRunning();
                lock (reqLock) Chat("ciao", null, 1);
                // anche le istruzioni della risposta suggerita (posto 2)
                Reply("ciao", "", 0.4, delegate (string t) { return false; });
            }
            catch { }
            // motore piccolo dei suggerimenti: si avvia dopo, per non rallentare il primo
            // e gli fa leggere la cornice fissa, così il primo testo grigio arriva subito
            if (SuggestEngine != null) { try { SuggestEngine.EnsureRunning(); SuggestEngine.Suggest("Ciao, ", ""); } catch { } }
        }

        // motore separato con un modello piccolo, solo per il testo grigio (molto più veloce); null = si usa questo
        public Engine SuggestEngine;

        // il motore dei suggerimenti ha impostazioni proprie che non si salvano (Dir nullo)
        private bool IsSuggestRole { get { return cfg.Dir == null; } }
        private int CompletionSlot { get { return IsSuggestRole ? 0 : 1; } }
        private int ReplySlot { get { return SuggestEngine != null ? 1 : 2; } }

        // ---- suggerimenti (posto 1 del motore, separato dalla correzione)
        private readonly object sugLock = new object();
        private int sugSeq;

        private const string ReplyPrompt =
            "Sei l'assistente di scrittura dell'utente. Scrivi in {LINGUA} il messaggio che l'utente invierà come risposta.\n" +
            "Regole:\n" +
            "1) Se c'è la BOZZA dell'utente, è lei che comanda: il messaggio deve dire esattamente quello che intende la bozza (stesso sì/no, stessi motivi), scritto meglio e COMPLETO: se la bozza si interrompe a metà frase, completa tu la frase in modo sensato. Non limitarti a ripetere la bozza e non cambiare idea al posto dell'utente.\n" +
            "2) Senza bozza, rispondi all'ultimo messaggio ricevuto.\n" +
            "3) Stesso tono della conversazione (tu o lei). Breve e naturale. Non inventare dati precisi (date, orari, prezzi, nomi) che non sono nel testo.\n" +
            "4) Scrivi SOLO il testo del messaggio, senza virgolette, titoli, spiegazioni o alternative.";

        // completamento della frase mentre si scrive: poche parole, il più in fretta possibile
        public string Suggest(string before, string context)
        {
            if (SuggestEngine != null && SuggestEngine.State != "errore")
            {
                try { return SuggestEngine.Suggest(before, context); }
                catch { }   // motore piccolo non disponibile: si usa quello principale
            }
            int my = Interlocked.Increment(ref sugSeq);
            if (string.IsNullOrEmpty(before) || !HasLetters(before)) return "";
            EnsureRunning();
            lock (sugLock)
            {
                if (my != sugSeq) return "";   // l'utente ha già scritto altro: richiesta superata
                if (before.Length > 1500) before = before.Substring(before.Length - 1500);
                string ctx = ProseContext(context);
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["prompt"] = SuggestPrompt(before, ctx);
                body["n_predict"] = 12;
                body["temperature"] = 0;
                body["n_probs"] = 1;   // probabilità di ogni parola: si mostra solo ciò di cui il modello è sicuro
                body["stop"] = new string[] { "\n", "---", "<|im_end|>", "<|endoftext|>" };
                body["id_slot"] = CompletionSlot;
                body["cache_prompt"] = true;
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                string resp = Http.PostJson(BaseUrl + "/completion", js.Serialize(body), apiKey, 20000);
                Dictionary<string, object> r = js.Deserialize<Dictionary<string, object>>(resp);
                object st, cp;
                bool limit = r.TryGetValue("stop_type", out st) && Convert.ToString(st) == "limit";
                List<KeyValuePair<string, double>> probs = new List<KeyValuePair<string, double>>();
                System.Collections.IEnumerable list = r.TryGetValue("completion_probabilities", out cp) ? cp as System.Collections.IEnumerable : null;
                if (list != null)
                {
                    foreach (object o in list)
                    {
                        Dictionary<string, object> t = o as Dictionary<string, object>;
                        object tok, lp;
                        if (t == null || !t.TryGetValue("token", out tok)) continue;
                        double p = 1;
                        if (t.TryGetValue("logprob", out lp) && lp != null) p = Math.Exp(Convert.ToDouble(lp, System.Globalization.CultureInfo.InvariantCulture));
                        else if (t.TryGetValue("prob", out lp) && lp != null) p = Convert.ToDouble(lp, System.Globalization.CultureInfo.InvariantCulture);
                        probs.Add(new KeyValuePair<string, double>(Convert.ToString(tok), p));
                    }
                }
                return GateSuggestion(before, ctx, probs, limit);
            }
        }

        // Cornice "messaggi ed email": il modello continua il testo come una persona che scrive,
        // invece di fare l'assistente o di continuare i menu della pagina.
        private string SuggestPrompt(string before, string ctx)
        {
            StringBuilder p = new StringBuilder();
            p.Append("Messaggi ed email scritti in ").Append(Lingue.Nome(cfg.Lingua)).Append(" corretto e naturale.\n\n");
            if (cfg.Lingua == "it")
                // esempi neutri: niente giorni, orari o nomi che il modello potrebbe ricopiare
                p.Append("---\nCiao, scusa se ti rispondo solo adesso: ho visto il messaggio e ti faccio sapere presto.\n")
                 .Append("---\nBuongiorno, ho ricevuto il documento e lo controllo oggi stesso. Cordiali saluti.\n");
            p.Append("---\n");
            if (ctx.Length > 0) p.Append("Messaggio ricevuto:\n").Append(ctx).Append("\n\nRisposta:\n");
            p.Append(before);
            return p.ToString();
        }

        // Il testo intorno al campo conta solo se sono frasi vere (una mail, una chat):
        // menu, pulsanti ed etichette ("Home Notizie Sport Meteo") vengono scartati.
        public static string ProseContext(string ctx)
        {
            if (string.IsNullOrEmpty(ctx)) return "";
            List<string> lines = new List<string>();
            foreach (string l in ctx.Replace("\r", "").Split('\n')) { string t = l.Trim(); if (t.Length > 0) lines.Add(t); }
            bool[] ok = new bool[lines.Count];
            int proseChars = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                MatchCollection w = Regex.Matches(lines[i], @"[^\W\d_]+");
                int caps = 0;
                foreach (Match m in w) if (char.IsUpper(m.Value[0])) caps++;
                ok[i] = w.Count >= 5 && caps < 0.4 * w.Count;   // i menu sono parole con l'iniziale maiuscola
                if (ok[i]) proseChars += lines[i].Length;
            }
            if (proseChars < 40) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                // saluti e firme corte attaccati a una frase ("Ciao Marco,", "Grazie, Luca")
                bool shortNear = lines[i].Split(' ').Length <= 3 && lines[i].Length < 30 &&
                    ((i > 0 && ok[i - 1]) || (i + 1 < lines.Count && ok[i + 1]));
                if (ok[i] || shortNear) { if (sb.Length > 0) sb.Append('\n'); sb.Append(lines[i]); }
            }
            string s = sb.ToString();
            return s.Length > 800 ? s.Substring(s.Length - 800) : s;
        }

        private static readonly HashSet<string> TailWords = new HashSet<string>(
            ("il lo la i gli le un uno una di a da in con su per tra fra e o ma che non mi ti si ci vi del della dei delle " +
             "al alla ai alle nel nella sul sulla mio mia tuo tua suo sua è").Split(' '));

        // Si tiene solo la parte di cui il modello è sicuro, a parole intere. Meglio nessun suggerimento
        // che uno a caso: niente parole appese in fondo, niente numeri (orari, prezzi) inventati.
        public static string GateSuggestion(string before, string ctx, List<KeyValuePair<string, double>> probs, bool cutByLimit)
        {
            const double FirstMin = 0.12, WordMin = 0.10, PunctMin = 0.30;
            bool midWord = before.Length > 0 && char.IsLetterOrDigit(before[before.Length - 1]);
            // parole: testo, probabilità delle lettere, punteggiatura finale e sua probabilità
            List<string> wt = new List<string>(), pt = new List<string>();
            List<double> wp = new List<double>(), pp = new List<double>();
            foreach (KeyValuePair<string, double> kv in probs)
            {
                string tok = kv.Key ?? "";
                if (tok.Trim().Length > 0 && Regex.IsMatch(tok, @"^[\s.,;:!?…]+$"))
                {
                    if (wt.Count > 0) { int k = wt.Count - 1; pt[k] += tok; pp[k] *= kv.Value; }
                    continue;
                }
                if (wt.Count == 0 || (tok.Length > 0 && char.IsWhiteSpace(tok[0])) || pt[wt.Count - 1].Length > 0)
                { wt.Add(tok); wp.Add(kv.Value); pt.Add(""); pp.Add(1); }
                else { int k = wt.Count - 1; wt[k] += tok; wp[k] *= kv.Value; }
            }
            if (cutByLimit && wt.Count > 0 && pt[wt.Count - 1].Length == 0) { int k = wt.Count - 1; wt.RemoveAt(k); wp.RemoveAt(k); pt.RemoveAt(k); pp.RemoveAt(k); }

            string known = before + " " + ctx;
            List<string> outW = new List<string>(), outP = new List<string>();
            double firstProb = 0;
            for (int i = 0; i < wt.Count; i++)
            {
                if (wp[i] < (i == 0 ? FirstMin : WordMin)) break;
                if (Regex.IsMatch(wt[i], @"\d") && known.IndexOf(wt[i].Trim(' ', '.', ','), StringComparison.Ordinal) < 0) break;
                if (i == 0) firstProb = wp[i];
                bool surePunct = pt[i].Length > 0 && pp[i] >= PunctMin;
                outW.Add(wt[i]);
                outP.Add(surePunct ? pt[i] : "");
                // fine frase, oppure il modello non sa se mettere la punteggiatura: ci si ferma qui
                if (pt[i].Length > 0 && (!surePunct || Regex.IsMatch(pt[i], "[.!?…]"))) break;
            }
            while (outW.Count > 0 && outP[outW.Count - 1].Length == 0 && TailWords.Contains(outW[outW.Count - 1].Trim().ToLowerInvariant()))
            { outW.RemoveAt(outW.Count - 1); outP.RemoveAt(outP.Count - 1); }
            if (outW.Count == 0) return "";

            bool completesWord = midWord && outW[0].Length > 0 && !char.IsWhiteSpace(outW[0][0]);
            bool closes = Regex.IsMatch(outP[outW.Count - 1], "[.!?…]");
            if (outW.Count < 2 && !(completesWord || closes || firstProb >= 0.30)) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < outW.Count; i++) sb.Append(outW[i]).Append(outP[i]);
            string s = sb.ToString();
            if (before.Length > 0 && char.IsWhiteSpace(before[before.Length - 1])) s = s.TrimStart();
            s = s.TrimEnd(' ', ',', ';', ':');
            return HasLetters(s) ? s : "";
        }

        private const string RewriteRules =
            "Regole: stesso significato, non aggiungere e non togliere informazioni, lascia identici nomi propri, numeri, link ed emoji, " +
            "mantieni gli a capo. Scrivi SOLO il testo finale, senza virgolette, titoli, spiegazioni o alternative.";

        // risposta suggerita, inviata a pezzi mentre il motore la scrive; onChunk false = interrompi
        public void Reply(string context, string draft, double temperature, Func<string, bool> onChunk)
        {
            Transform("rispondi", draft, context, temperature, onChunk);
        }

        // modalità che riscrivono o scrivono testo (tutte tranne "correggi"), sempre a pezzi mentre il motore scrive
        public void Transform(string mode, string text, string context, double temperature, Func<string, bool> onChunk)
        {
            Interlocked.Increment(ref sugSeq);   // i completamenti in attesa vengono scartati
            EnsureRunning();
            text = text ?? "";
            if (text.Length > 8000) text = text.Substring(0, 8000);
            lock (sugLock)
            {
                string lingua = Lingue.Nome(cfg.Lingua);
                string system;
                StringBuilder u = new StringBuilder();
                int maxTokens = Math.Min(3000, text.Length / 2 + 200);
                if (mode == "completa")
                {
                    system = "Sei l'assistente di scrittura dell'utente. Continua il testo dell'utente dal punto esatto in cui si interrompe, " +
                        "seguendo il senso, il tono e lo stile. Scrivi SOLO la continuazione, senza ripetere il testo già scritto: " +
                        "di solito basta finire la frase in corso, al massimo una frase in più; grammatica corretta, nella stessa lingua del testo. " +
                        "Non inventare dati precisi (date, orari, prezzi, nomi).";
                    if (!string.IsNullOrEmpty(context))
                    {
                        if (context.Length > 2000) context = context.Substring(context.Length - 2000);
                        u.Append("Contesto (messaggio ricevuto o testo vicino):\n<<<\n").Append(context).Append("\n>>>\n\n");
                    }
                    u.Append("Testo da continuare:\n<<<\n").Append(text).Append("\n>>>");
                    maxTokens = 60;
                }
                else if (mode == "formale")
                {
                    system = "Sei l'assistente di scrittura dell'utente. Riscrivi il testo in modo più formale, cortese e professionale, nella stessa lingua del testo. " + RewriteRules;
                    u.Append("<<<\n").Append(text).Append("\n>>>");
                }
                else if (mode == "semplice")
                {
                    system = "Sei l'assistente di scrittura dell'utente. Riscrivi il testo in modo più semplice, chiaro e breve, nella stessa lingua del testo. " + RewriteRules;
                    u.Append("<<<\n").Append(text).Append("\n>>>");
                }
                else if (mode == "traduci")
                {
                    system = "Sei un traduttore professionista. Traduci il testo in " + Lingue.Nome(cfg.TraduciIn) + ", in modo naturale e corretto, " +
                        "mantenendo tono e formattazione. Lascia identici nomi propri, numeri, link ed emoji. Scrivi SOLO la traduzione, senza spiegazioni.";
                    u.Append("<<<\n").Append(text).Append("\n>>>");
                    maxTokens = Math.Min(3000, text.Length + 200);
                }
                else
                {
                    system = ReplyPrompt.Replace("{LINGUA}", lingua);
                    if (!string.IsNullOrEmpty(context))
                    {
                        if (context.Length > 3000) context = context.Substring(context.Length - 3000);
                        u.Append("Conversazione o pagina (le ultime righe sono le più recenti):\n<<<\n").Append(context).Append("\n>>>\n\n");
                    }
                    if (text.Trim().Length > 0)
                        u.Append("BOZZA dell'utente:\n<<<\n").Append(text.Trim()).Append("\n>>>\n\nScrivi il messaggio dell'utente seguendo la bozza.");
                    else u.Append("Scrivi la risposta dell'utente.");
                    maxTokens = 220;
                }

                List<object> messages = new List<object>();
                messages.Add(Msg("system", system));
                messages.Add(Msg("user", u.ToString()));
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["model"] = "locale";
                body["messages"] = messages;
                body["temperature"] = temperature;
                body["top_p"] = 0.9;
                body["max_tokens"] = maxTokens;
                body["stream"] = true;
                body["id_slot"] = ReplySlot;
                Dictionary<string, object> kw = new Dictionary<string, object>();
                kw["enable_thinking"] = false;
                body["chat_template_kwargs"] = kw;
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                bool started = false;
                Http.PostJsonLines(BaseUrl + "/v1/chat/completions", js.Serialize(body), apiKey, 120000, delegate (string line)
                {
                    if (!line.StartsWith("data: ")) return true;
                    string d = line.Substring(6).Trim();
                    if (d == "[DONE]") return false;
                    try
                    {
                        Dictionary<string, object> ev = js.Deserialize<Dictionary<string, object>>(d);
                        System.Collections.IList ch = ev["choices"] as System.Collections.IList;
                        if (ch == null || ch.Count == 0) return true;
                        Dictionary<string, object> delta = ((Dictionary<string, object>)ch[0])["delta"] as Dictionary<string, object>;
                        object content;
                        if (delta == null || !delta.TryGetValue("content", out content) || content == null) return true;
                        string t = Convert.ToString(content);
                        if (!started) { t = t.TrimStart(); if (t.Length == 0) return true; started = true; }
                        return onChunk(t);
                    }
                    catch { return true; }
                });
            }
        }

        // ---- correzione
        public string Correct(string text)
        {
            if (string.IsNullOrEmpty(text) || !HasLetters(text)) return text;
            EnsureRunning();
            lock (reqLock)
            {
                try { return CorrectAll(text); }
                catch (WebException ex)
                {
                    if (ex.Response != null || ExternalUrl != null) throw new EngineException("Errore del motore: " + ex.Message);
                    // il processo potrebbe essere caduto: un solo nuovo tentativo
                    Stop();
                    EnsureRunning();
                    try { return CorrectAll(text); }
                    catch (WebException ex2) { throw new EngineException("Errore del motore: " + ex2.Message); }
                }
            }
        }

        private string CorrectAll(string text)
        {
            StringBuilder sb = new StringBuilder();
            Stopwatch busy = Stopwatch.StartNew();
            foreach (string ch in Chunker.Split(text, 700))
            {
                ThermalGuard();
                sb.Append(CorrectChunk(ch, true));
                // testi lunghi: piccola pausa ogni tanto, così CPU e GPU non restano al 100% a lungo
                if (cfg.PauseAfterSeconds > 0 && busy.Elapsed.TotalSeconds > cfg.PauseAfterSeconds)
                {
                    CoolingDown = true;
                    Thread.Sleep(4000);
                    CoolingDown = false;
                    busy.Reset();
                    busy.Start();
                }
            }
            return sb.ToString();
        }

        // Protezione temperatura GPU (NVIDIA: lettura reale con nvidia-smi)
        private void ThermalGuard()
        {
            if (ExternalUrl != null || runningLayers <= 0 || cfg.Backend != "cuda") return;
            int t = GpuTemp.Read();
            LastTemp = t;
            if (t < 0 || t < cfg.TempMax) return;
            CoolingDown = true;
            try
            {
                DateTime until = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < until)
                {
                    Thread.Sleep(2000);
                    t = GpuTemp.Read();
                    LastTemp = t;
                    if (t >= 0 && t <= cfg.TempMax - 8) return;
                }
                // ancora calda dopo 45 secondi: si continua sul processore finché non si raffredda
                lock (startLock)
                {
                    HeatCpuMode = true;
                    State = "avvio";
                    if (!(StartAndWait(exePath, modelPath, false, 0) || StartAndWait(exePath, modelPath, true, 0)))
                    {
                        State = "errore";
                        throw new EngineException("GPU troppo calda e avvio su processore non riuscito. " + LastError);
                    }
                    State = "pronto";
                }
            }
            finally { CoolingDown = false; }
        }

        private string CorrectChunk(string chunk, bool allowLineSplit)
        {
            int a = 0;
            while (a < chunk.Length && char.IsWhiteSpace(chunk[a])) a++;
            int b = chunk.Length;
            while (b > a && char.IsWhiteSpace(chunk[b - 1])) b--;
            string core = chunk.Substring(a, b - a);
            if (!HasLetters(core)) return chunk;

            List<SpellHit> hits = Speller.Find(core);
            string fixedCore = Clean(Chat(core, hits), core);

            // le righe devono restare le stesse: altrimenti si corregge riga per riga
            if (CountLines(fixedCore) != CountLines(core))
            {
                if (allowLineSplit && core.IndexOf('\n') >= 0)
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in Chunker.Lines(core)) sb.Append(CorrectChunk(line, false));
                    return chunk.Substring(0, a) + sb.ToString() + chunk.Substring(b);
                }
                fixedCore = core;
            }

            // sicurezza: se la risposta e' troppo diversa, si tiene l'originale
            double ratio = (double)fixedCore.Length / Math.Max(1, core.Length);
            double lo = core.Length < 15 ? 0.5 : 0.65;
            double hi = core.Length < 15 ? 2.0 : 1.45;
            if (fixedCore.Length == 0 || ratio < lo || ratio > hi) fixedCore = core;

            // garanzia: le parole che il dizionario segnala e che l'AI ha lasciato sbagliate vengono corrette
            fixedCore = Speller.ApplyFixes(fixedCore, hits);

            return chunk.Substring(0, a) + fixedCore + chunk.Substring(b);
        }

        private static string Wrap(string t) { return "<<<\n" + t + "\n>>>"; }

        private string Chat(string text, List<SpellHit> hits, int maxTokens = 0)
        {
            List<object> messages = new List<object>();
            if (cfg.Lingua == "it")
            {
                messages.Add(Msg("system", SystemPrompt));
                foreach (string[] ex in Examples)
                {
                    messages.Add(Msg("user", Wrap(ex[0])));
                    messages.Add(Msg("assistant", ex[1]));
                }
            }
            else
            {
                // altre lingue: stesse regole, senza gli esempi in italiano
                string lingua = Lingue.Nome(cfg.Lingua);
                messages.Add(Msg("system", SystemPrompt.Replace("per la lingua italiana", "per la lingua " + lingua) +
                    "\n6) Il testo è in " + lingua + ": rispondi in " + lingua + ", non tradurre in italiano."));
            }
            string user = Wrap(text);
            if (hits != null && hits.Count > 0)
            {
                StringBuilder h = new StringBuilder("\n\n(Nota: il dizionario di Windows segnala come parole sbagliate: ");
                for (int i = 0; i < hits.Count && i < 25; i++)
                {
                    if (i > 0) h.Append(", ");
                    h.Append(hits[i].Word);
                    if (hits[i].Suggestions.Count > 0) h.Append(" \u2192 ").Append(string.Join(" / ", hits[i].Suggestions.ToArray()));
                }
                h.Append(". Correggile, salvo nomi propri o termini tecnici.)");
                user += h.ToString();
            }
            messages.Add(Msg("user", user));

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = "locale";
            body["messages"] = messages;
            body["temperature"] = 0.1;
            body["top_p"] = 0.9;
            body["max_tokens"] = maxTokens > 0 ? maxTokens : Math.Min(3000, text.Length / 2 + 96);
            body["stream"] = false;
            body["id_slot"] = 0;
            Dictionary<string, object> kw = new Dictionary<string, object>();
            kw["enable_thinking"] = false;
            body["chat_template_kwargs"] = kw;

            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            string resp = Http.PostJson(BaseUrl + "/v1/chat/completions", js.Serialize(body), apiKey, 300000);
            Dictionary<string, object> r = js.Deserialize<Dictionary<string, object>>(resp);
            object[] choices = r["choices"] as object[];
            if (choices == null) { System.Collections.ArrayList al = r["choices"] as System.Collections.ArrayList; if (al != null) choices = al.ToArray(); }
            Dictionary<string, object> c0 = (Dictionary<string, object>)choices[0];
            Dictionary<string, object> msg = (Dictionary<string, object>)c0["message"];
            object content;
            return msg.TryGetValue("content", out content) && content != null ? Convert.ToString(content) : "";
        }

        private static Dictionary<string, object> Msg(string role, string content)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["role"] = role;
            d["content"] = content;
            return d;
        }

        public static string Clean(string output, string original)
        {
            if (output == null) return "";
            string s = Regex.Replace(output, "<think>.*?</think>", "", RegexOptions.Singleline);
            int endThink = s.IndexOf("</think>", StringComparison.Ordinal);
            if (endThink >= 0) s = s.Substring(endThink + 8);
            s = s.Replace("\r\n", "\n");
            s = s.Trim('\n', ' ', '\t');
            if (s.StartsWith("<<<")) s = s.Substring(3).TrimStart('\n', ' ');
            if (s.EndsWith(">>>")) s = s.Substring(0, s.Length - 3).TrimEnd('\n', ' ');
            s = Regex.Replace(s, "^(testo corretto|ecco il testo corretto|correzione)\\s*:\\s*", "", RegexOptions.IgnoreCase);
            if (s.StartsWith("```") && !original.StartsWith("```"))
            {
                s = Regex.Replace(s, "^```[a-zA-Z]*\\n?", "");
                s = Regex.Replace(s, "\\n?```$", "");
            }
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '“') && (s[s.Length - 1] == '"' || s[s.Length - 1] == '”')
                && !(original.StartsWith("\"") || original.StartsWith("“")))
                s = s.Substring(1, s.Length - 2);
            // ripristina gli a capo di Windows se l'originale li usava
            if (original.IndexOf("\r\n", StringComparison.Ordinal) >= 0) s = s.Replace("\n", "\r\n");
            return s;
        }

        private static int CountLines(string s)
        {
            int n = 1;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }

        public static bool HasLetters(string s)
        {
            foreach (char c in s) if (char.IsLetter(c)) return true;
            return false;
        }
    }


    // ------------------------------------------------------------------ DIZIONARIO ITALIANO DI WINDOWS (Spell Checking API)
    internal sealed class SpellHit
    {
        public string Word;
        public List<string> Suggestions = new List<string>();
    }

    internal static class Speller
    {
        [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellCheckerFactory
        {
            System.Runtime.InteropServices.ComTypes.IEnumString GetSupportedLanguages();
            [return: MarshalAs(UnmanagedType.Bool)] bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
            ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag);
        }

        [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellChecker
        {
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetLanguageTag();
            IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);
            System.Runtime.InteropServices.ComTypes.IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
        }

        [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IEnumSpellingError
        {
            [PreserveSig] int Next(out ISpellingError value);
        }

        [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISpellingError
        {
            uint GetStartIndex();
            uint GetLength();
            int GetCorrectiveAction();   // 0 nessuna, 1 suggerimenti, 2 sostituisci, 3 elimina
            [return: MarshalAs(UnmanagedType.LPWStr)] string GetReplacement();
        }

        private static readonly Guid FactoryClsid = new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC");
        public static volatile int Status = 0;   // 0 sconosciuto, 1 dizionario disponibile, -1 non disponibile
        private static string[] tags = new string[] { "it-IT", "it" };

        // dizionario di Windows nella lingua di scrittura scelta
        public static void SetLanguage(string code)
        {
            tags = new string[] { Lingue.Tag(code), code };
            Status = 0;
        }

        private static ISpellChecker Create()
        {
            // API disponibile solo su Windows 8 e successivi
            OperatingSystem os = Environment.OSVersion;
            if (os.Platform != PlatformID.Win32NT || os.Version < new Version(6, 2)) { Status = -1; return null; }
            try
            {
                Type t = Type.GetTypeFromCLSID(FactoryClsid, false);
                if (t == null) { Status = -1; return null; }
                ISpellCheckerFactory f = (ISpellCheckerFactory)Activator.CreateInstance(t);
                foreach (string tag in tags)
                {
                    if (f.IsSupported(tag)) { Status = 1; return f.CreateSpellChecker(tag); }
                }
                Status = -1;
            }
            catch { Status = -1; }
            return null;
        }

        public static bool Available()
        {
            if (Status == 0) { ISpellChecker c = Create(); if (c != null) Marshal.ReleaseComObject(c); }
            return Status == 1;
        }

        // parole sbagliate secondo il dizionario (senza nomi in maiuscolo, sigle, link, numeri)
        public static List<SpellHit> Find(string text)
        {
            List<SpellHit> res = new List<SpellHit>();
            if (string.IsNullOrEmpty(text) || Status == -1) return res;
            ISpellChecker sc = null;
            try
            {
                sc = Create();
                if (sc == null) return res;
                IEnumSpellingError en = sc.Check(text);
                HashSet<string> seen = new HashSet<string>();
                ISpellingError err;
                int guard = 0;
                while (guard++ < 500 && en.Next(out err) == 0 && err != null)
                {
                    int start = (int)err.GetStartIndex(), len = (int)err.GetLength();
                    int action = err.GetCorrectiveAction();
                    string repl = action == 2 ? err.GetReplacement() : null;
                    Marshal.ReleaseComObject(err);
                    if (start < 0 || len <= 0 || start + len > text.Length) continue;
                    string w = text.Substring(start, len);
                    if (!seen.Add(w) || Skip(text, start, w)) continue;
                    SpellHit h = new SpellHit();
                    h.Word = w;
                    if (!string.IsNullOrEmpty(repl)) h.Suggestions.Add(repl);
                    else if (action == 1)
                    {
                        System.Runtime.InteropServices.ComTypes.IEnumString sugg = sc.Suggest(w);
                        string[] one = new string[1];
                        while (h.Suggestions.Count < 3 && sugg.Next(1, one, IntPtr.Zero) == 0) h.Suggestions.Add(one[0]);
                        Marshal.ReleaseComObject(sugg);
                    }
                    res.Add(h);
                }
                Marshal.ReleaseComObject(en);
            }
            catch { }
            finally { try { if (sc != null) Marshal.ReleaseComObject(sc); } catch { } }
            return res;
        }

        private static bool Skip(string text, int start, string w)
        {
            if (w.Length < 2) return true;
            foreach (char c in w) if (char.IsDigit(c) || c == '@' || c == '/' || c == '_') return true;
            if (w.ToUpperInvariant() == w) return true;                  // sigle (WP, HTML...)
            // parole dentro link o email
            int s = text.LastIndexOfAny(new char[] { ' ', '\n', '\t' }, Math.Max(0, start - 1)) + 1;
            int e = text.IndexOfAny(new char[] { ' ', '\n', '\t' }, start);
            string tok = text.Substring(s, (e < 0 ? text.Length : e) - s);
            if (tok.Contains("://") || tok.Contains("www.") || tok.Contains("@")) return true;
            return false;
        }

        // si applica il primo suggerimento solo se la parola è rimasta sbagliata e il suggerimento è vicino
        public static string ApplyFixes(string text, List<SpellHit> hits)
        {
            if (hits == null || hits.Count == 0 || string.IsNullOrEmpty(text)) return text;
            foreach (SpellHit h in hits)
            {
                if (h.Suggestions.Count == 0) continue;
                string cand = h.Suggestions[0];
                bool capital = char.IsUpper(h.Word[0]);
                bool onlyAccents = Fold(cand) == Fold(h.Word);
                // nomi propri (maiuscola) intoccabili, tranne correzioni di soli accenti/apostrofi (Perche -> Perché)
                if (capital && !onlyAccents) continue;
                if (!onlyAccents && Distance(h.Word.ToLowerInvariant(), cand.ToLowerInvariant()) > 2) continue;
                if (!onlyAccents && h.Word.Length < 4) continue;
                text = ReplaceWord(text, h.Word, cand);
            }
            return text;
        }

        private static string ReplaceWord(string text, string word, string repl)
        {
            string pattern = "(?<![\\p{L}\\p{N}'’])" + Regex.Escape(word) + "(?![\\p{L}\\p{N}'’])";
            return Regex.Replace(text, pattern, delegate (Match m) { return repl; });
        }

        private static string Fold(string s)
        {
            string d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            StringBuilder b = new StringBuilder();
            foreach (char c in d)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (c == '\'' || c == '’' || c == ' ') continue;
                b.Append(c);
            }
            return b.ToString();
        }

        private static int Distance(string a, string b)
        {
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            return d[a.Length, b.Length];
        }

        public static string SelfTest()
        {
            List<SpellHit> hits = new List<SpellHit>();
            Func<string, string, SpellHit> H = delegate (string w, string sug) { SpellHit x = new SpellHit(); x.Word = w; x.Suggestions.Add(sug); return x; };
            hits.Add(H("spazzio", "spazio"));
            hits.Add(H("Perche", "Perché"));
            hits.Add(H("Arzano", "Arazzo"));          // nome proprio: non va toccato
            hits.Add(H("ecceziunale", "eccezionale"));
            hits.Add(H("xyzqw", "casa"));            // suggerimento troppo lontano: non va applicato
            string input = "Perche c'è poco spazzio ad Arzano? Lo spazzio è ecceziunale, xyzqw.";
            return input + "\n=> " + ApplyFixes(input, hits);
        }
    }

    internal static class GpuTemp
    {
        private static DateTime last = DateTime.MinValue;
        private static int lastVal = -1;
        private static string smi;
        private static readonly object sync = new object();

        // temperatura massima tra le GPU NVIDIA, -1 se non leggibile
        public static int Read()
        {
            lock (sync)
            {
                if ((DateTime.UtcNow - last).TotalSeconds < 2) return lastVal;
                last = DateTime.UtcNow;
                lastVal = -1;
                try
                {
                    if (smi == null)
                    {
                        string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
                        smi = File.Exists(p) ? p : "";
                    }
                    if (smi.Length == 0) return -1;
                    ProcessStartInfo psi = new ProcessStartInfo(smi, "--query-gpu=temperature.gpu --format=csv,noheader,nounits");
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    using (Process p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(3000);
                        foreach (string l in o.Split('\n'))
                        {
                            int v;
                            if (int.TryParse(l.Trim(), out v) && v > lastVal) lastVal = v;
                        }
                    }
                }
                catch { lastVal = -1; }
                return lastVal;
            }
        }
    }

    internal static class Chunker
    {
        public static List<string> Lines(string text)
        {
            List<string> res = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                int n = text.IndexOf('\n', i);
                if (n < 0) { res.Add(text.Substring(i)); break; }
                res.Add(text.Substring(i, n - i + 1));
                i = n + 1;
            }
            return res;
        }

        public static List<string> Split(string text, int max)
        {
            List<string> units = new List<string>();
            foreach (string p in Lines(text))
            {
                if (p.Length <= max) { units.Add(p); continue; }
                int s = 0;
                for (int k = 0; k < p.Length - 1; k++)
                {
                    char c = p[k];
                    bool end = c == '.' || c == '!' || c == '?' || c == '…' || c == ';';
                    if (end && p[k + 1] == ' ' && (k + 2 - s) >= max / 3)
                    {
                        units.Add(p.Substring(s, k + 2 - s));
                        s = k + 2;
                    }
                }
                if (s < p.Length) units.Add(p.Substring(s));
            }
            List<string> res = new List<string>();
            StringBuilder cur = new StringBuilder();
            foreach (string u in units)
            {
                if (cur.Length > 0 && cur.Length + u.Length > max) { res.Add(cur.ToString()); cur.Length = 0; }
                cur.Append(u);
            }
            if (cur.Length > 0) res.Add(cur.ToString());
            return res;
        }
    }

    internal static class Http
    {
        public static int Status(string url, int timeoutMs)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Method = "GET";
                r.Timeout = timeoutMs;
                r.Proxy = null;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse()) return (int)resp.StatusCode;
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                return resp != null ? (int)resp.StatusCode : -1;
            }
            catch { return -1; }
        }

        public static string PostJson(string url, string json, string apiKey, int timeoutMs)
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = "POST";
            r.ContentType = "application/json; charset=utf-8";
            r.Timeout = timeoutMs;
            r.ReadWriteTimeout = timeoutMs;
            r.Proxy = null;
            if (!string.IsNullOrEmpty(apiKey)) r.Headers["Authorization"] = "Bearer " + apiKey;
            byte[] data = Encoding.UTF8.GetBytes(json);
            r.ContentLength = data.Length;
            using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // risposta a righe (streaming SSE): onLine restituisce false per interrompere
        public static void PostJsonLines(string url, string json, string apiKey, int timeoutMs, Func<string, bool> onLine)
        {
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = "POST";
            r.ContentType = "application/json; charset=utf-8";
            r.Timeout = timeoutMs;
            r.ReadWriteTimeout = timeoutMs;
            r.Proxy = null;
            if (!string.IsNullOrEmpty(apiKey)) r.Headers["Authorization"] = "Bearer " + apiKey;
            byte[] data = Encoding.UTF8.GetBytes(json);
            r.ContentLength = data.Length;
            using (Stream s = r.GetRequestStream()) s.Write(data, 0, data.Length);
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                    if (!onLine(line)) { r.Abort(); return; }
            }
        }
    }

    // ------------------------------------------------------------------ SERVER LOCALE (per l'estensione)
    internal sealed class LocalServer
    {
        private readonly int port;
        private readonly Engine engine;
        private TcpListener listener;
        public Func<bool> IsActive = delegate { return true; };
        public Func<string> ModelName = delegate { return ""; };
        public Action<string, string> SetLanguages;   // lingua di scrittura, lingua della traduzione (null = invariata)

        public LocalServer(int port, Engine engine)
        {
            this.port = port;
            this.engine = engine;
        }

        public void Start()
        {
            listener = new TcpListener(IPAddress.Loopback, port);   // solo questo PC
            listener.Start();
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public void Stop()
        {
            try { if (listener != null) listener.Stop(); } catch { }
        }

        private void Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(delegate { Handle(c); });
            }
        }

        private static int Find(byte[] buf, int len, byte[] pat)
        {
            for (int i = 0; i <= len - pat.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < pat.Length; j++) if (buf[i + j] != pat[j]) { ok = false; break; }
                if (ok) return i;
            }
            return -1;
        }

        private void Handle(TcpClient c)
        {
            using (c)
            {
                try
                {
                    c.ReceiveTimeout = 15000;
                    NetworkStream ns = c.GetStream();
                    MemoryStream ms = new MemoryStream();
                    byte[] b = new byte[8192];
                    byte[] crlf2 = new byte[] { 13, 10, 13, 10 };
                    int headerEnd = -1;
                    while (headerEnd < 0)
                    {
                        int n = ns.Read(b, 0, b.Length);
                        if (n <= 0) return;
                        ms.Write(b, 0, n);
                        headerEnd = Find(ms.GetBuffer(), (int)ms.Length, crlf2);
                        if (ms.Length > 65536) return;
                    }
                    string head = Encoding.ASCII.GetString(ms.GetBuffer(), 0, headerEnd);
                    string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                    string[] first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0].ToUpperInvariant();
                    string path = first[1];
                    int q = path.IndexOf('?');
                    if (q >= 0) path = path.Substring(0, q);
                    Dictionary<string, string> hd = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 1; i < lines.Length; i++)
                    {
                        int k = lines[i].IndexOf(':');
                        if (k > 0) hd[lines[i].Substring(0, k).Trim()] = lines[i].Substring(k + 1).Trim();
                    }
                    int len = 0;
                    string cl;
                    if (hd.TryGetValue("Content-Length", out cl)) int.TryParse(cl, out len);
                    if (len > 400000) { Send(ns, 413, "{\"error\":\"Testo troppo lungo\"}", null); return; }
                    int have = (int)ms.Length - (headerEnd + 4);
                    byte[] body = new byte[Math.Max(0, len)];
                    if (have > 0) Array.Copy(ms.GetBuffer(), headerEnd + 4, body, 0, Math.Min(have, len));
                    int got = Math.Min(have, len);
                    while (got < len)
                    {
                        int n = ns.Read(body, got, len - got);
                        if (n <= 0) break;
                        got += n;
                    }

                    // Solo estensioni del browser o programmi locali: le pagine web vengono rifiutate.
                    string origin;
                    hd.TryGetValue("Origin", out origin);
                    if (!string.IsNullOrEmpty(origin) &&
                        !(origin.StartsWith("chrome-extension://") || origin.StartsWith("moz-extension://") || origin.StartsWith("extension://")))
                    {
                        Send(ns, 403, "{\"error\":\"Origine non consentita\"}", null);
                        return;
                    }

                    JavaScriptSerializer js = new JavaScriptSerializer();
                    js.MaxJsonLength = int.MaxValue;

                    if (method == "OPTIONS") { Send(ns, 204, "", origin); return; }

                    if (method == "GET" && (path == "/status" || path == "/"))
                    {
                        Dictionary<string, object> st = new Dictionary<string, object>();
                        st["ok"] = true;
                        st["app"] = "CorrettoreIT";
                        st["versione"] = Program.Version;
                        st["attivo"] = IsActive();
                        st["stato"] = engine.State;
                        st["modello"] = ModelName();
                        st["ultimaScorciatoia"] = Program.LastHotkeyTiming;
                        st["lingua"] = engine.Cfg.Lingua;
                        st["traduciIn"] = engine.Cfg.TraduciIn;
                        List<object> langs = new List<object>();
                        foreach (string[] l in Lingue.Elenco) langs.Add(new string[] { l[0], l[1] });
                        st["lingue"] = langs;
                        Send(ns, 200, js.Serialize(st), origin);
                        return;
                    }

                    if (method == "POST" && path == "/correct")
                    {
                        if (!IsActive())
                        {
                            Send(ns, 503, "{\"error\":\"Correttore disattivato: riattivalo dall'icona vicino all'orologio di Windows.\"}", origin);
                            return;
                        }
                        string text = null;
                        try
                        {
                            Dictionary<string, object> req = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body, 0, got));
                            object t;
                            if (req != null && req.TryGetValue("text", out t) && t != null) text = Convert.ToString(t);
                        }
                        catch { }
                        if (text == null) { Send(ns, 400, "{\"error\":\"Richiesta non valida\"}", origin); return; }
                        if (text.Length > 20000) { Send(ns, 413, "{\"error\":\"Testo troppo lungo (massimo 20.000 caratteri)\"}", origin); return; }

                        Stopwatch sw = Stopwatch.StartNew();
                        try
                        {
                            string corrected = engine.Correct(text);
                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["corrected"] = corrected;
                            res["ms"] = sw.ElapsedMilliseconds;
                            Send(ns, 200, js.Serialize(res), origin);
                        }
                        catch (Exception ex)
                        {
                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["error"] = ex.Message;
                            Send(ns, 500, js.Serialize(res), origin);
                        }
                        return;
                    }

                    if (method == "POST" && path == "/impostazioni")
                    {
                        // lingua di scrittura e lingua della traduzione, scelte dal popup dell'estensione
                        Dictionary<string, object> req = null;
                        try { req = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body, 0, got)); } catch { }
                        if (req == null) { Send(ns, 400, "{\"error\":\"Richiesta non valida\"}", origin); return; }
                        object v;
                        string lingua = req.TryGetValue("lingua", out v) && v != null ? Convert.ToString(v) : null;
                        string trad = req.TryGetValue("traduciIn", out v) && v != null ? Convert.ToString(v) : null;
                        if (SetLanguages != null) SetLanguages(lingua, trad);
                        Send(ns, 200, "{\"ok\":true}", origin);
                        return;
                    }

                    if (method == "POST" && (path == "/suggest" || path == "/reply"))
                    {
                        if (!IsActive())
                        {
                            Send(ns, 503, "{\"error\":\"Correttore disattivato: riattivalo dall'icona vicino all'orologio di Windows.\"}", origin);
                            return;
                        }
                        Dictionary<string, object> req = null;
                        try { req = js.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body, 0, got)); } catch { }
                        if (req == null) { Send(ns, 400, "{\"error\":\"Richiesta non valida\"}", origin); return; }
                        Func<string, string> field = delegate (string k) { object v; return req.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : ""; };

                        if (path == "/suggest")
                        {
                            Stopwatch sw = Stopwatch.StartNew();
                            try
                            {
                                Dictionary<string, object> res = new Dictionary<string, object>();
                                res["text"] = engine.Suggest(field("before"), field("context"));
                                res["ms"] = sw.ElapsedMilliseconds;
                                Send(ns, 200, js.Serialize(res), origin);
                            }
                            catch (Exception ex)
                            {
                                Dictionary<string, object> res = new Dictionary<string, object>();
                                res["error"] = ex.Message;
                                Send(ns, 500, js.Serialize(res), origin);
                            }
                            return;
                        }

                        // /reply: testo inviato a pezzi mentre viene scritto (text/plain senza lunghezza fissa)
                        try { engine.EnsureRunning(); }
                        catch (Exception ex)
                        {
                            Dictionary<string, object> res = new Dictionary<string, object>();
                            res["error"] = ex.Message;
                            Send(ns, 500, js.Serialize(res), origin);
                            return;
                        }
                        double temp;
                        if (!double.TryParse(field("temperature"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out temp)
                            || temp <= 0 || temp > 1.5) temp = 0.4;
                        // modalità: rispondi (predefinita), completa, formale, semplice, traduci
                        string mode = field("mode");
                        if (!Modalita.Valida(mode) || mode == "correggi") mode = "rispondi";
                        string text = mode == "rispondi" ? field("draft") : field("text");
                        if (mode == "rispondi" && text.Length == 0) text = field("text");
                        SendStreamHead(ns, origin);
                        c.SendTimeout = 5000;
                        try
                        {
                            engine.Transform(mode, text, field("context"), temp, delegate (string piece)
                            {
                                try
                                {
                                    byte[] pb = Encoding.UTF8.GetBytes(piece);
                                    ns.Write(pb, 0, pb.Length);
                                    ns.Flush();
                                    return true;
                                }
                                catch { return false; }   // l'estensione ha chiuso: si ferma il motore
                            });
                        }
                        catch { }
                        return;
                    }

                    Send(ns, 404, "{\"error\":\"Non trovato\"}", origin);
                }
                catch { }
            }
        }

        private static void SendStreamHead(Stream s, string origin)
        {
            StringBuilder h = new StringBuilder();
            h.Append("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\n");
            if (!string.IsNullOrEmpty(origin))
            {
                h.Append("Access-Control-Allow-Origin: ").Append(origin).Append("\r\n");
                h.Append("Access-Control-Allow-Private-Network: true\r\n");
            }
            h.Append("\r\n");
            byte[] hb = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(hb, 0, hb.Length);
            s.Flush();
        }

        private static void Send(Stream s, int code, string json, string origin)
        {
            string reason = code == 200 ? "OK" : code == 204 ? "No Content" : code == 400 ? "Bad Request" : code == 403 ? "Forbidden"
                : code == 404 ? "Not Found" : code == 413 ? "Payload Too Large" : code == 503 ? "Service Unavailable" : "Error";
            byte[] data = Encoding.UTF8.GetBytes(json ?? "");
            StringBuilder h = new StringBuilder();
            h.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            if (data.Length > 0) h.Append("Content-Type: application/json; charset=utf-8\r\n");
            h.Append("Content-Length: ").Append(data.Length).Append("\r\n");
            h.Append("Cache-Control: no-store\r\nConnection: close\r\n");
            if (!string.IsNullOrEmpty(origin))
            {
                h.Append("Access-Control-Allow-Origin: ").Append(origin).Append("\r\n");
                h.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
                h.Append("Access-Control-Allow-Headers: Content-Type\r\n");
                h.Append("Access-Control-Allow-Private-Network: true\r\n");
            }
            h.Append("\r\n");
            byte[] hb = Encoding.ASCII.GetBytes(h.ToString());
            s.Write(hb, 0, hb.Length);
            if (data.Length > 0) s.Write(data, 0, data.Length);
            s.Flush();
        }
    }

    // ------------------------------------------------------------------ ICONA DI SISTEMA
    internal sealed class TrayApp : ApplicationContext
    {
        private readonly string baseDir;
        private readonly Config cfg;
        private readonly Engine engine;
        private readonly LocalServer server;
        private readonly NotifyIcon tray;
        private readonly Icon iconOn, iconOff;
        private readonly ToolStripMenuItem miState, miActive, miStartup, miHotkey, miSpell;
        private readonly HotkeyWindow hotkeyWin;
        private readonly Control invoker;
        private readonly Osd osd;
        private readonly System.Windows.Forms.Timer refresh;
        private bool hotkeyOk;
        private volatile bool busy;
        private readonly FloatButton bubble, pill;
        private readonly System.Windows.Forms.Timer pillTimer;
        private MouseHook mouseHook;
        private PreviewForm preview;
        private IntPtr pillFg;
        private readonly ToolStripMenuItem miBubble, miPill, miLang, miTrad;

        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string RunName = "CorrettoreIT";

        public TrayApp(string dir, Config c)
        {
            baseDir = dir;
            cfg = c;
            invoker = new Control();
            invoker.CreateControl();
            IntPtr force = invoker.Handle;

            engine = new Engine(cfg, baseDir);
            // testo grigio più veloce: modello piccolo in un secondo motore, se installato
            if (cfg.SuggestModel.Length > 0 && File.Exists(Path.IsPathRooted(cfg.SuggestModel) ? cfg.SuggestModel : Path.Combine(baseDir, cfg.SuggestModel)))
                engine.SuggestEngine = new Engine(cfg.ForSuggestions(), baseDir);
            KillStrayServers();

            iconOn = LoadIcon("icona.ico");
            iconOff = LoadIcon("icona-off.ico");

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem title = new ToolStripMenuItem("Correttore IT " + Program.Version + (cfg.ModelName.Length > 0 ? "  ·  " + cfg.ModelName : ""));
            title.Enabled = false;
            miState = new ToolStripMenuItem("Stato: ...");
            miState.Enabled = false;
            miSpell = new ToolStripMenuItem("Dizionario italiano di Windows");
            miSpell.Enabled = false;
            miActive = new ToolStripMenuItem("Attivo", null, delegate { ToggleActive(); });
            miHotkey = new ToolStripMenuItem("Correggi testo selezionato: " + cfg.Hotkey);
            miHotkey.Enabled = false;
            ToolStripMenuItem miClip = new ToolStripMenuItem("Correggi il testo copiato (appunti)", null, delegate { CorrectClipboard(); });
            miBubble = new ToolStripMenuItem("Pulsante galleggiante (tutti i programmi)", null, delegate { ToggleBubble(); });
            miPill = new ToolStripMenuItem("Mini pulsante sul testo selezionato", null, delegate { TogglePill(); });
            miLang = new ToolStripMenuItem("Lingua di scrittura");
            miTrad = new ToolStripMenuItem("Traduci in");
            foreach (string[] l in Lingue.Elenco)
            {
                string code = l[0];
                miLang.DropDownItems.Add(new ToolStripMenuItem(l[1], null, delegate { SetLanguages(code, null); }) { Tag = code });
                miTrad.DropDownItems.Add(new ToolStripMenuItem(l[1], null, delegate { SetLanguages(null, code); }) { Tag = code });
            }
            miStartup = new ToolStripMenuItem("Avvia con Windows", null, delegate { ToggleStartup(); });
            ToolStripMenuItem miChrome = new ToolStripMenuItem("Collega a Google Chrome...", null, delegate { OpenChromeSetup(); });
            ToolStripMenuItem miExport = new ToolStripMenuItem("Crea copia portatile (zip)...", null, delegate { ExportPackage(); });
            ToolStripMenuItem miFolder = new ToolStripMenuItem("Apri cartella del programma", null, delegate { Shell("explorer.exe", "\"" + baseDir.TrimEnd('\\') + "\""); });
            ToolStripMenuItem miUninstall = new ToolStripMenuItem("Elimina il programma da questo PC...", null, delegate { Uninstall(); });
            ToolStripMenuItem miExit = new ToolStripMenuItem("Esci", null, delegate { ExitThread(); });

            menu.Items.Add(title);
            menu.Items.Add(miState);
            menu.Items.Add(miSpell);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miActive);
            menu.Items.Add(miHotkey);
            menu.Items.Add(miClip);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miBubble);
            menu.Items.Add(miPill);
            menu.Items.Add(miLang);
            menu.Items.Add(miTrad);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miStartup);
            menu.Items.Add(miChrome);
            menu.Items.Add(miExport);
            menu.Items.Add(miFolder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miUninstall);
            menu.Items.Add(miExit);
            menu.Opening += delegate { UpdateUi(); };

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.Icon = cfg.Active ? iconOn : iconOff;
            tray.Visible = true;
            // clic sull'icona: corregge il testo copiato (tasto destro -> Copia nel programma) e lo rimette negli appunti
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) CorrectClipboard(); };

            osd = new Osd();
            Speller.SetLanguage(cfg.Lingua);

            // pulsante galleggiante: clic = anteprima, tasto destro = modalità, trascinalo per spostarlo
            bubble = new FloatButton(48, Ui.Blue, Ui.Violet,
                "Correttore IT: clic = correggi o scrivi la risposta · tasto destro = tutte le modalità · trascina per spostarlo", true);
            bubble.Clicked += delegate (IntPtr fg) { StartFromWindow(fg, null); };
            bubble.MenuRequested += delegate (IntPtr fg, Point p) { ShowModeMenu(fg, p, true); };
            bubble.Moved += delegate (Point p) { cfg.BollaX = p.X; cfg.BollaY = p.Y; cfg.Save(); };
            if (cfg.Bolla) ShowBubble();

            // mini pulsante accanto al testo appena selezionato col mouse
            pill = new FloatButton(30, Ui.Violet, Color.FromArgb(192, 38, 211), "Correttore IT: correggi, completa, rispondi, traduci…", false);
            pill.Clicked += delegate (IntPtr fg) { pillTimer.Stop(); pill.Hide(); ShowModeMenu(pillFg, Cursor.Position, false); };
            pill.MenuRequested += delegate (IntPtr fg, Point p) { pillTimer.Stop(); pill.Hide(); ShowModeMenu(pillFg, p, false); };
            pillTimer = new System.Windows.Forms.Timer();
            pillTimer.Interval = 3500;
            pillTimer.Tick += delegate { pillTimer.Stop(); pill.Hide(); };
            if (cfg.MiniSelezione) StartMouseHook();

            server = new LocalServer(cfg.Port, engine);
            server.IsActive = delegate { return cfg.Active; };
            server.ModelName = delegate { return cfg.ModelName; };
            server.SetLanguages = delegate (string l, string t) { invoker.BeginInvoke((MethodInvoker)delegate { SetLanguages(l, t); }); };
            try { server.Start(); }
            catch (Exception ex)
            {
                tray.ShowBalloonTip(6000, "Correttore IT", "La porta " + cfg.Port + " è occupata: Chrome non potrà collegarsi.\n" + ex.Message, ToolTipIcon.Warning);
            }

            hotkeyWin = new HotkeyWindow(OnHotkey);
            hotkeyOk = hotkeyWin.Register(cfg.Hotkey);
            if (!hotkeyOk)
                tray.ShowBalloonTip(6000, "Correttore IT", "La scorciatoia " + cfg.Hotkey + " è già usata da un altro programma. Puoi cambiarla in config.json.", ToolTipIcon.Warning);

            refresh = new System.Windows.Forms.Timer();
            refresh.Interval = 1500;
            refresh.Tick += delegate { UpdateUi(); bubble.KeepOnTop(); };
            refresh.Start();
            UpdateUi();

            if (cfg.Active) new Thread(delegate () { engine.Warmup(); }) { IsBackground = true }.Start();

            new Thread(delegate ()
            {
                if (!Speller.Available())
                    invoker.BeginInvoke((MethodInvoker)delegate
                    {
                        tray.ShowBalloonTip(10000, "Correttore IT - manca il dizionario italiano",
                            "Per il controllo ortografico aggiungi la lingua Italiano in Windows: Impostazioni > Ora e lingua > Lingua.", ToolTipIcon.Warning);
                    });
            }) { IsBackground = true }.Start();

            if (Program.FirstRun)
                tray.ShowBalloonTip(8000, "Correttore IT è pronto",
                    "Seleziona un testo in qualsiasi programma e premi " + cfg.Hotkey + ".\nTasto destro sull'icona per attivare/disattivare.", ToolTipIcon.Info);
        }

        private Icon LoadIcon(string name)
        {
            try
            {
                string p = Path.Combine(Path.Combine(baseDir, "app"), name);
                if (File.Exists(p)) return new Icon(p);
            }
            catch { }
            return SystemIcons.Application;
        }

        private void KillStrayServers()
        {
            string llamaDir = Path.Combine(baseDir, "llama");
            foreach (Process p in Process.GetProcessesByName("llama-server"))
            {
                try
                {
                    if (p.MainModule.FileName.StartsWith(llamaDir, StringComparison.OrdinalIgnoreCase)) p.Kill();
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        private string StateText()
        {
            if (!cfg.Active) return "disattivato (RAM libera)";
            switch (engine.State)
            {
                case "avvio": return "avvio del motore AI...";
                case "pronto":
                    if (engine.CoolingDown) return "pausa di raffreddamento" + (engine.LastTemp >= 0 ? " (GPU " + engine.LastTemp + "°C)" : "");
                    if (engine.HeatCpuMode) return "GPU calda: lavoro sul processore finché si raffredda";
                    if (engine.UsingCpuFallback) return "motore AI pronto (GPU non disponibile: uso il processore)";
                    return "motore AI pronto" + (engine.LastTemp >= 0 ? " · GPU " + engine.LastTemp + "°C" : "");
                case "errore": return "errore: " + engine.LastError;
                default: return "attivo (il motore parte al primo uso)";
            }
        }

        private void UpdateUi()
        {
            string st = StateText();
            miState.Text = "Stato: " + (st.Length > 70 ? st.Substring(0, 70) + "..." : st);
            string ln = Lingue.Nome(cfg.Lingua);
            miSpell.Text = Speller.Status == 1 ? "Dizionario " + ln + " di Windows: attivo" : Speller.Status == -1 ? "Dizionario " + ln + ": NON installato (vedi LEGGIMI)" : "Dizionario " + ln + " di Windows";
            miActive.Checked = cfg.Active;
            miBubble.Checked = cfg.Bolla;
            miPill.Checked = cfg.MiniSelezione;
            miLang.Text = "Lingua di scrittura: " + ln;
            miTrad.Text = "Traduci in: " + Lingue.Nome(cfg.TraduciIn);
            foreach (ToolStripMenuItem i in miLang.DropDownItems) i.Checked = (string)i.Tag == cfg.Lingua;
            foreach (ToolStripMenuItem i in miTrad.DropDownItems) i.Checked = (string)i.Tag == cfg.TraduciIn;
            miStartup.Checked = IsStartup();
            miHotkey.Text = "Correggi testo selezionato: " + cfg.Hotkey + (hotkeyOk ? "" : " (non disponibile)");
            tray.Icon = cfg.Active ? iconOn : iconOff;
            string tip = "Correttore IT - " + st;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        private void ToggleActive()
        {
            cfg.Active = !cfg.Active;
            cfg.Save();
            if (!cfg.Active) new Thread(delegate () { engine.Stop(); }) { IsBackground = true }.Start();
            else new Thread(delegate () { engine.Warmup(); }) { IsBackground = true }.Start();
            UpdateUi();
            osd.ShowText(cfg.Active ? "Correttore IT attivato" : "Correttore IT disattivato", 1500);
        }

        // ---- pulsante galleggiante, mini pulsante, lingue
        private void ShowBubble()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Point p = new Point(cfg.BollaX, cfg.BollaY);
            bool onScreen = false;
            foreach (Screen s in Screen.AllScreens) if (s.WorkingArea.Contains(p)) onScreen = true;
            if (cfg.BollaX < 0 || !onScreen) p = new Point(wa.Right - Ui.S(84), wa.Bottom - Ui.S(150));
            bubble.ShowAt(p);
        }

        private void ToggleBubble()
        {
            cfg.Bolla = !cfg.Bolla;
            cfg.Save();
            if (cfg.Bolla) ShowBubble(); else bubble.Hide();
            UpdateUi();
        }

        private void TogglePill()
        {
            cfg.MiniSelezione = !cfg.MiniSelezione;
            cfg.Save();
            if (cfg.MiniSelezione) StartMouseHook();
            else { if (mouseHook != null) { mouseHook.Dispose(); mouseHook = null; } pill.Hide(); }
            UpdateUi();
        }

        private void StartMouseHook()
        {
            if (mouseHook != null) return;
            mouseHook = new MouseHook();
            mouseHook.Down += delegate (Point p) { if (pill.Visible && !pill.Bounds.Contains(p)) { pillTimer.Stop(); pill.Hide(); } };
            mouseHook.Selected += delegate (Point p) { invoker.BeginInvoke((MethodInvoker)delegate { OnMouseSelection(p); }); };
        }

        // programmi in cui il mini pulsante non serve (browser: c'è l'estensione) o darebbe fastidio
        private static readonly string[] PillSkip = { "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "explorer", "searchhost", "startmenuexperiencehost", "shellexperiencehost" };

        private void OnMouseSelection(Point p)
        {
            if (!cfg.MiniSelezione || !cfg.Active || busy) return;
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || Native.IsOwnWindow(fg)) return;
            string cls = Native.ClassOf(fg);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return;
            if (Array.IndexOf(PillSkip, Native.ProcessNameOf(fg)) >= 0) return;
            pillFg = fg;
            pill.ShowAt(new Point(p.X + Ui.S(10), p.Y + Ui.S(8)));
            pillTimer.Stop();
            pillTimer.Start();
        }

        private void SetLanguages(string lingua, string traduciIn)
        {
            if (lingua != null)
            {
                cfg.Lingua = Lingue.Valida(lingua, cfg.Lingua);
                Speller.SetLanguage(cfg.Lingua);
                new Thread(delegate () { Speller.Available(); }) { IsBackground = true }.Start();
            }
            if (traduciIn != null) cfg.TraduciIn = Lingue.Valida(traduciIn, cfg.TraduciIn);
            cfg.Save();
            UpdateUi();
        }

        // menu con tutte le modalità (tasto destro sul pulsante, clic sul mini pulsante)
        private void ShowModeMenu(IntPtr fg, Point at, bool withSettings)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.ShowImageMargin = false;
            foreach (string[] md in Modalita.Elenco)
            {
                string code = md[0];
                string label = code == "traduci" ? "Traduci in " + Lingue.Nome(cfg.TraduciIn) : md[1];
                m.Items.Add(new ToolStripMenuItem(label, null, delegate { StartFromWindow(fg, code); }));
            }
            if (withSettings)
            {
                m.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem lang = new ToolStripMenuItem("Lingua di scrittura: " + Lingue.Nome(cfg.Lingua));
                foreach (string[] l in Lingue.Elenco)
                {
                    string code = l[0];
                    ToolStripMenuItem it = new ToolStripMenuItem(l[1], null, delegate { SetLanguages(code, null); });
                    it.Checked = code == cfg.Lingua;
                    lang.DropDownItems.Add(it);
                }
                m.Items.Add(lang);
                m.Items.Add(new ToolStripMenuItem("Nascondi il pulsante (si riattiva dall'icona vicino all'orologio)", null, delegate { ToggleBubble(); }));
            }
            m.Closed += delegate { BeginInvokeLater(delegate { m.Dispose(); }); };
            m.Show(at);
        }

        private void BeginInvokeLater(MethodInvoker a)
        {
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 500;
            t.Tick += delegate { t.Stop(); t.Dispose(); a(); };
            t.Start();
        }

        // prende il testo dal programma in cui si sta scrivendo e apre l'anteprima
        private void StartFromWindow(IntPtr fg, string mode)
        {
            if (busy) { osd.ShowText("Attendi: operazione già in corso...", 1500); return; }
            if (!cfg.Active) { osd.ShowText("Correttore IT è disattivato (icona vicino all'orologio)", 2500); return; }
            if (fg == IntPtr.Zero || Native.IsOwnWindow(fg))
            {
                osd.ShowText("Prima clicca nel testo del programma in cui stai scrivendo, poi sul pulsante", 3000);
                return;
            }
            busy = true;
            string sel = null, prev = null;
            bool all = false;
            try
            {
                Native.WaitModifiersReleased(1500);
                if (!Native.BringToFront(fg))
                {
                    osd.ShowText("Non riesco a leggere il testo: clicca nel programma e riprova", 3000);
                    return;
                }
                ClipboardBackup backup = ClipboardBackup.Take();
                prev = backup.Text;
                sel = CopySelection();
                if (string.IsNullOrEmpty(sel) || sel.Trim().Length == 0)
                {
                    // niente selezionato: si prende tutto il testo del campo
                    for (int attempt = 0; attempt < 2 && (string.IsNullOrEmpty(sel) || sel.Trim().Length == 0); attempt++)
                    {
                        if (attempt > 0) { for (int i = 0; i < 8; i++) { Thread.Sleep(25); Application.DoEvents(); } }
                        Native.SendCtrl(Keys.A);
                        Thread.Sleep(60);
                        sel = CopySelection();
                    }
                    all = !string.IsNullOrEmpty(sel) && sel.Trim().Length > 0;
                    if (!all) sel = "";
                }
                backup.Restore();
            }
            finally { busy = false; }

            if (sel.Length > 20000) { osd.ShowText("Testo troppo lungo (massimo 20.000 caratteri): selezionane una parte", 3000); return; }
            string ctx = prev != null && prev.Trim().Length > 0 && prev.Trim() != sel.Trim() && prev.Length <= 6000 ? prev : "";
            if (mode == null) mode = sel.Trim().Length == 0 ? "rispondi" : "correggi";
            OpenPreview(fg, sel, all, ctx, mode);
        }

        private void OpenPreview(IntPtr fg, string original, bool all, string ctx, string mode)
        {
            try { if (preview != null && !preview.IsDisposed) preview.Close(); } catch { }
            preview = new PreviewForm(engine, cfg, original, ctx, mode, Native.CaretPoint(fg));
            preview.InsertRequested += delegate (string text) { InsertResult(fg, original, text, all); };
            preview.Show();
        }

        // rimette il testo nel programma: sostituisce la selezione (o tutto il campo) con Ctrl+V
        private void InsertResult(IntPtr fg, string original, string text, bool all)
        {
            busy = true;
            try
            {
                Native.WaitModifiersReleased(1500);
                bool front = Native.BringToFront(fg);
                Program.LastHotkeyTiming = "inserimento: primo piano " + (front ? "ok" : "NO") + ", finestra " + Native.ClassOf(fg) + " / attuale " + Native.ClassOf(Native.GetForegroundWindow()) +
                    ", tutto=" + all + ", lunghezza " + text.Length;
                if (!front)
                {
                    try { Clipboard.SetDataObject(text, true, 10, 50); } catch { }
                    osd.ShowText("Non riesco a tornare al programma: il testo è negli appunti (clicca nel testo e premi Ctrl+V)", 5000);
                    return;
                }
                // il programma appena tornato in primo piano rimette il cursore nel testo: gli si lascia un attimo
                for (int i = 0; i < 6; i++) { Thread.Sleep(25); Application.DoEvents(); }
                ClipboardBackup backup = ClipboardBackup.Take();
                if (original.Trim().Length > 0)
                {
                    // si incolla solo dopo aver controllato che sia selezionato proprio il testo di partenza:
                    // così i tasti non finiscono altrove mentre il programma sta ancora riprendendo il cursore
                    bool ok = false;
                    for (int attempt = 0; attempt < 4 && !ok; attempt++)
                    {
                        if (attempt > 0) { for (int i = 0; i < 8; i++) { Thread.Sleep(25); Application.DoEvents(); } }
                        if (all) { Native.SendCtrl(Keys.A); Thread.Sleep(40); ok = SameText(CopySelection(), original); }
                        else ok = EnsureSelection(original);
                    }
                    Program.LastHotkeyTiming += ", selezione " + (ok ? "ok" : "NO");
                    if (!ok)
                    {
                        try { Clipboard.SetDataObject(text, true, 10, 50); } catch { }
                        osd.ShowText("La selezione è andata persa: il testo è negli appunti (seleziona e premi Ctrl+V)", 5000);
                        return;
                    }
                }
                if (!PutOnClipboard(text))
                {
                    osd.ShowText("Gli appunti sono occupati da un altro programma: riprova", 4000);
                    return;
                }
                Native.SendCtrl(Keys.V);
                osd.ShowText("✓ Fatto  (Ctrl+Z per annullare)", 2000);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 1500;
                t.Tick += delegate { t.Stop(); t.Dispose(); backup.Restore(); };
                t.Start();
            }
            finally { busy = false; }
        }

        // mette il testo negli appunti e controlla che ci sia davvero: subito dopo una copia alcuni programmi
        // (es. Blocco note di Windows 11) tengono gli appunti occupati e la scrittura fallisce in silenzio
        private static bool PutOnClipboard(string text)
        {
            for (int i = 0; i < 20; i++)
            {
                try { Clipboard.SetDataObject(text, true, 3, 30); } catch { }
                bool ok = false;
                try { ok = Clipboard.ContainsText() && Clipboard.GetText() == text; } catch { }
                if (ok)
                {
                    // il programma che ha appena copiato deve accorgersi che gli appunti sono cambiati,
                    // altrimenti con Ctrl+V incolla la sua copia del testo vecchio
                    for (int k = 0; k < 6; k++) { Thread.Sleep(25); Application.DoEvents(); }
                    return true;
                }
                for (int k = 0; k < 4; k++) { Thread.Sleep(15); Application.DoEvents(); }
            }
            return false;
        }

        private bool IsStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        private void ToggleStartup()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (IsStartup()) k.DeleteValue(RunName, false);
                    else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                }
            }
            catch { }
            UpdateUi();
        }

        private static void Shell(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }); } catch { }
        }

        private void OpenChromeSetup()
        {
            string ext = Path.Combine(baseDir, "estensione-chrome");
            try { Clipboard.SetText(ext); } catch { }
            Shell("explorer.exe", "\"" + ext + "\"");
            try { Process.Start(new ProcessStartInfo("chrome", "chrome://extensions") { UseShellExecute = true }); } catch { }
            MessageBox.Show(
                "Per collegare Google Chrome (una volta sola):\n\n" +
                "1. In Chrome apri  chrome://extensions\n" +
                "2. Attiva \"Modalità sviluppatore\" (in alto a destra)\n" +
                "3. Clicca \"Carica estensione non pacchettizzata\"\n" +
                "4. Scegli la cartella:\n   " + ext + "\n   (il percorso è già negli appunti: incollalo con Ctrl+V)\n\n" +
                "Fatto: nei campi di testo comparirà il pulsante di correzione.",
                "Correttore IT - Chrome", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- scorciatoia globale
        private void OnHotkey()
        {
            if (busy) { osd.ShowText("Correzione già in corso...", 1500); return; }
            if (!cfg.Active) { osd.ShowText("Correttore IT è disattivato (icona vicino all'orologio)", 2500); return; }
            busy = true;   // subito: durante l'attesa degli appunti si elaborano i messaggi di Windows

            IntPtr fg = Native.GetForegroundWindow();
            Stopwatch sw = Stopwatch.StartNew();
            Native.WaitModifiersReleased(1500);
            long tKeys = sw.ElapsedMilliseconds;
            ClipboardBackup backup = ClipboardBackup.Take();
            long tBackup = sw.ElapsedMilliseconds;

            string sel = CopySelection();
            if (string.IsNullOrEmpty(sel) || sel.Trim().Length == 0)
            {
                busy = false;
                backup.Restore();
                osd.ShowText("Seleziona prima il testo da correggere", 2000);
                return;
            }
            if (sel.Length > 20000)
            {
                busy = false;
                backup.Restore();
                osd.ShowText("Testo troppo lungo (massimo 20.000 caratteri)", 2500);
                return;
            }

            long tCopy = sw.ElapsedMilliseconds;
            osd.ShowText(engine.State == "pronto" ? "Correzione in corso..." : "Avvio del motore AI e correzione...", 0);
            string original = sel;
            Thread t = new Thread(delegate ()
            {
                string result = null, err = null;
                try { result = engine.Correct(original); }
                catch (Exception ex) { err = ex.Message; }
                long tAi = sw.ElapsedMilliseconds;
                invoker.BeginInvoke((MethodInvoker)delegate
                {
                    long tUi = sw.ElapsedMilliseconds;
                    FinishHotkey(fg, original, result, err, backup);
                    Program.LastHotkeyTiming = string.Format("tasti {0} ms, appunti {1} ms, copia {2} ms, AI {3} ms, attesa {4} ms, incolla {5} ms",
                        tKeys, tBackup - tKeys, tCopy - tBackup, tAi - tCopy, tUi - tAi, sw.ElapsedMilliseconds - tUi);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void FinishHotkey(IntPtr fg, string original, string result, string err, ClipboardBackup backup)
        {
            busy = false;
            if (err != null) { backup.Restore(); osd.ShowText("Errore: " + err, 5000); return; }
            if (result == original) { backup.Restore(); osd.ShowText("✓ Nessun errore trovato", 1800); return; }

            if (Native.GetForegroundWindow() == fg)
            {
                // Ctrl+V sostituisce solo se il testo è ancora selezionato: altrimenti lo aggiungerebbe accanto al cursore
                Native.WaitModifiersReleased(1500);
                if (!EnsureSelection(original))
                {
                    try { Clipboard.SetDataObject(result, true, 10, 50); } catch { }
                    osd.ShowText("La selezione è andata persa: il testo corretto è negli appunti (seleziona e premi Ctrl+V)", 5000);
                    return;
                }
                if (!PutOnClipboard(result))
                {
                    osd.ShowText("Gli appunti sono occupati da un altro programma: riprova", 4000);
                    return;
                }
                Native.SendCtrl(Keys.V);
                osd.ShowText("✓ Testo corretto  (Ctrl+Z per annullare)", 2200);
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                t.Interval = 1500;
                t.Tick += delegate { t.Stop(); t.Dispose(); backup.Restore(); };
                t.Start();
            }
            else
            {
                try { Clipboard.SetDataObject(result, true, 10, 50); } catch { }
                osd.ShowText("Hai cambiato finestra: il testo corretto è negli appunti (Ctrl+V)", 4000);
            }
        }

        // Ctrl+C e attesa del nuovo contenuto degli appunti. Gli appunti non vengono svuotati prima e durante
        // l'attesa si rispondono i messaggi di Windows: altrimenti il programma che copia (es. Blocco note)
        // resta bloccato fino a 5 secondi aspettando il proprietario precedente degli appunti.
        private static string CopySelection()
        {
            uint seq = Native.GetClipboardSequenceNumber();
            Native.SendCtrl(Keys.C);
            Stopwatch w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < 1000)
            {
                Application.DoEvents();
                if (Native.GetClipboardSequenceNumber() != seq)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try { if (Clipboard.ContainsText()) return Clipboard.GetText(); } catch { }
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    return null;
                }
                Thread.Sleep(5);
            }
            return null;
        }

        private static bool SameText(string a, string b)
        {
            if (a == null || b == null) return false;
            return a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();
        }

        // quante frecce servono per attraversare il testo (a capo e emoji contano come un passo)
        private static int CaretSteps(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                else if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                n++;
            }
            return n;
        }

        // controlla che il testo copiato sia ancora selezionato; se la selezione si è persa la ricrea
        // (di solito il cursore è rimasto alla fine del testo, a volte all'inizio)
        private static bool EnsureSelection(string original)
        {
            if (SameText(CopySelection(), original)) return true;
            int steps = CaretSteps(original);
            if (steps > 3000) return false;
            Native.SendShiftArrows(Keys.Left, steps);
            if (SameText(CopySelection(), original)) return true;
            Native.SendArrow(Keys.Right);          // torna al punto di partenza
            Native.SendShiftArrows(Keys.Right, steps);
            if (SameText(CopySelection(), original)) return true;
            Native.SendArrow(Keys.Left);
            return false;
        }

        private void CorrectClipboard()
        {
            if (!cfg.Active) { osd.ShowText("Correttore IT è disattivato", 2000); return; }
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrEmpty(text)) { osd.ShowText("Negli appunti non c'è testo", 2000); return; }
            if (busy) { osd.ShowText("Correzione già in corso...", 1500); return; }
            busy = true;
            osd.ShowText("Correzione degli appunti in corso...", 0);
            Thread t = new Thread(delegate ()
            {
                string result = null, err = null;
                try { result = engine.Correct(text); }
                catch (Exception ex) { err = ex.Message; }
                invoker.BeginInvoke((MethodInvoker)delegate
                {
                    busy = false;
                    if (err != null) { osd.ShowText("Errore: " + err, 5000); return; }
                    if (result == text) { osd.ShowText("✓ Nessun errore trovato", 1800); return; }
                    try { Clipboard.SetDataObject(result, true, 10, 50); } catch { }
                    osd.ShowText("✓ Testo corretto copiato negli appunti", 2500);
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        // Il programma vive tutto nella sua cartella: la copia portatile \u00e8 semplicemente la cartella in uno zip.
        // Su un altro PC si estrae e si avvia CorrettoreIT.exe (Prepara.bat sceglie il motore per la sua scheda video).
        private void ExportPackage()
        {
            if (busy) { osd.ShowText("Attendi la fine della correzione in corso", 2000); return; }
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Title = "Salva la copia portatile";
            dlg.Filter = "Archivio zip (*.zip)|*.zip";
            dlg.FileName = "CorrettoreIT-portatile.zip";
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (dlg.ShowDialog() != DialogResult.OK) return;
            string target = dlg.FileName;
            string root = baseDir.TrimEnd('\\');
            if (target.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Scegli una cartella fuori da quella del programma.", "Correttore IT");
                return;
            }

            busy = true;
            osd.ShowText("Creazione della copia portatile... (circa un minuto)", 0);
            Thread t = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    using (ZipArchive za = ZipFile.Open(target, ZipArchiveMode.Create))
                    {
                        foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                        {
                            if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            // modelli e zip dei motori sono gi\u00e0 compressi: si copiano e basta (molto pi\u00f9 veloce)
                            CompressionLevel lvl = ext == ".gguf" || ext == ".zip" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                            za.CreateEntryFromFile(f, "CorrettoreIT/" + f.Substring(root.Length + 1).Replace('\\', '/'), lvl);
                        }
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                invoker.BeginInvoke((MethodInvoker)delegate
                {
                    busy = false;
                    if (err != null) { osd.ShowText("Errore: " + err, 5000); return; }
                    osd.ShowText("\u2713 Copia portatile creata", 2500);
                    Shell("explorer.exe", "/select,\"" + target + "\"");
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Uninstall()
        {
            if (MessageBox.Show("Vuoi eliminare Correttore IT da questo PC?\n\nVerr\u00e0 cancellata la cartella\n" + baseDir.TrimEnd('\\') +
                "\n(programma, modelli AI e impostazioni) e tolto l'avvio automatico.",
                "Elimina Correttore IT", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) { if (k != null) k.DeleteValue(RunName, false); } } catch { }
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Correttore IT.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
            engine.Stop();
            server.Stop();

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                    "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + baseDir.TrimEnd('\\') + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WorkingDirectory = Path.GetTempPath();
                Process.Start(psi);
            }
            catch { }

            MessageBox.Show("Correttore IT è stato rimosso.\n\nRicorda di rimuovere anche l'estensione da Chrome (chrome://extensions).",
                "Correttore IT", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ExitThread();
        }

        protected override void ExitThreadCore()
        {
            try { refresh.Stop(); } catch { }
            try { hotkeyWin.Unregister(); } catch { }
            try { server.Stop(); } catch { }
            try { engine.Stop(); } catch { }
            try { tray.Visible = false; tray.Dispose(); } catch { }
            try { osd.Close(); } catch { }
            try { if (mouseHook != null) mouseHook.Dispose(); } catch { }
            try { bubble.Close(); pill.Close(); } catch { }
            try { if (preview != null && !preview.IsDisposed) preview.Close(); } catch { }
            base.ExitThreadCore();
        }
    }

    // ------------------------------------------------------------------ APPUNTI (salva e ripristina)
    internal sealed class ClipboardBackup
    {
        private DataObject data;

        // testo che c'era negli appunti (es. il messaggio ricevuto copiato prima)
        public string Text
        {
            get
            {
                try { return data != null && data.GetDataPresent(DataFormats.UnicodeText) ? Convert.ToString(data.GetData(DataFormats.UnicodeText)) : null; }
                catch { return null; }
            }
        }

        public static ClipboardBackup Take()
        {
            ClipboardBackup b = new ClipboardBackup();
            try
            {
                IDataObject d = Clipboard.GetDataObject();
                if (d == null) return b;
                DataObject copy = new DataObject();
                bool any = false;
                foreach (string f in new string[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf, DataFormats.Html, DataFormats.Bitmap, DataFormats.FileDrop })
                {
                    try
                    {
                        if (!d.GetDataPresent(f, false)) continue;
                        object v = d.GetData(f, false);
                        if (v != null) { copy.SetData(f, v); any = true; }
                    }
                    catch { }
                }
                if (any) b.data = copy;
            }
            catch { }
            return b;
        }

        public void Restore()
        {
            try
            {
                if (data != null) Clipboard.SetDataObject(data, true, 10, 50);
                else Clipboard.Clear();
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ GRAFICA (scala dello schermo, forme arrotondate)
    internal static class Ui
    {
        public static float Scale = 1f;   // 1 = 100%, 1.25 = 125%...
        public static int S(float v) { return (int)Math.Round(v * Scale); }
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color Violet = Color.FromArgb(124, 58, 237);
        public static readonly Color Ink = Color.FromArgb(15, 23, 42);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Line = Color.FromArgb(226, 232, 240);
        public static readonly Color Soft = Color.FromArgb(241, 245, 249);

        public static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float rad)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // stellina a quattro punte (simbolo dell'AI)
        public static void Sparkle(Graphics g, Brush b, float cx, float cy, float r)
        {
            float k = r * 0.22f;
            PointF[] pts = new PointF[]
            {
                new PointF(cx, cy - r), new PointF(cx + k, cy - k), new PointF(cx + r, cy), new PointF(cx + k, cy + k),
                new PointF(cx, cy + r), new PointF(cx - k, cy + k), new PointF(cx - r, cy), new PointF(cx - k, cy - k)
            };
            using (System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                p.AddCurve(pts, 0.25f);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        // pallino con sfumatura blu-viola e stelline (pulsante e intestazione della finestra)
        public static void Badge(Graphics g, RectangleF r, Color a, Color b, bool hover)
        {
            if (hover) { a = ControlPaint.Light(a, 0.2f); b = ControlPaint.Light(b, 0.2f); }
            using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(r, a, b, 45f))
                g.FillEllipse(br, r);
            using (SolidBrush w = new SolidBrush(Color.White))
            {
                float cx = r.X + r.Width * 0.46f, cy = r.Y + r.Height * 0.54f;
                Sparkle(g, w, cx, cy, r.Width * 0.24f);
                Sparkle(g, w, r.X + r.Width * 0.70f, r.Y + r.Height * 0.30f, r.Width * 0.10f);
            }
        }
    }

    // ------------------------------------------------------------------ PULSANTE GALLEGGIANTE (non prende il focus)
    // Finestra "a strati": bordo liscio, ombra morbida. Cliccandola il programma in cui si scrive resta in primo piano.
    internal sealed class FloatButton : Form
    {
        public event Action<IntPtr> Clicked;              // finestra in primo piano al momento del clic
        public event Action<IntPtr, Point> MenuRequested; // tasto destro
        public event Action<Point> Moved;                 // trascinato in una nuova posizione
        private readonly Color c1, c2;
        private readonly bool draggable;
        private readonly int diameter, margin;
        private Point downAt, startLoc;
        private bool down, dragging, hover;
        private IntPtr fgAtDown;
        private readonly ToolTip tip = new ToolTip();

        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte op, flags, alpha, format; }
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Native.POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref Native.POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);

        public FloatButton(int size, Color c1, Color c2, string tooltip, bool draggable)
        {
            this.c1 = c1;
            this.c2 = c2;
            this.draggable = draggable;
            diameter = Ui.S(size);
            margin = Ui.S(6);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(diameter + margin * 2, diameter + margin * 2);
            Cursor = Cursors.Hand;
            tip.SetToolTip(this, tooltip);
            MouseEnter += delegate { hover = true; Render(); };
            MouseLeave += delegate { hover = false; Render(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | 0x00000008; // LAYERED | TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0021) { m.Result = new IntPtr(3); return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Render(); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (IsHandleCreated) Render(); }

        // disegna il pulsante con trasparenza vera (bordi lisci e ombra) e lo passa a Windows
        private void Render()
        {
            if (!IsHandleCreated) return;
            using (Bitmap bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    // ombra morbida
                    for (int i = margin; i > 0; i--)
                    {
                        int a = (int)(26.0 * (margin - i + 1) / margin);
                        using (SolidBrush sb = new SolidBrush(Color.FromArgb(a, 15, 23, 42)))
                            g.FillEllipse(sb, margin - i, margin - i + Ui.S(2), diameter + i * 2, diameter + i * 2);
                    }
                    RectangleF r = new RectangleF(margin, margin, diameter, diameter);
                    Ui.Badge(g, r, c1, c2, hover || down);
                    using (Pen p = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(1.5f, Ui.Scale * 1.5f)))
                        g.DrawEllipse(p, r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f);
                }
                IntPtr screen = GetDC(IntPtr.Zero);
                IntPtr mem = CreateCompatibleDC(screen);
                IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
                IntPtr old = SelectObject(mem, hbmp);
                try
                {
                    SIZE sz = new SIZE(); sz.cx = Width; sz.cy = Height;
                    Native.POINT src = new Native.POINT();
                    Native.POINT dst = new Native.POINT(); dst.X = Left; dst.Y = Top;
                    BLENDFUNCTION bf = new BLENDFUNCTION(); bf.op = 0; bf.alpha = 255; bf.format = 1;   // AC_SRC_ALPHA
                    UpdateLayeredWindow(Handle, screen, ref dst, ref sz, mem, ref src, 0, ref bf, 2);   // ULW_ALPHA
                }
                finally
                {
                    SelectObject(mem, old);
                    DeleteObject(hbmp);
                    DeleteDC(mem);
                    ReleaseDC(IntPtr.Zero, screen);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            fgAtDown = Native.GetForegroundWindow();
            if (e.Button == MouseButtons.Left)
            {
                down = true;
                dragging = false;
                downAt = Cursor.Position;
                startLoc = Location;
                Render();
            }
            else if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(fgAtDown, Cursor.Position);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!down || !draggable) return;
            Point p = Cursor.Position;
            int dx = p.X - downAt.X, dy = p.Y - downAt.Y;
            if (!dragging && dx * dx + dy * dy > 25) dragging = true;
            if (dragging) Location = new Point(startLoc.X + dx, startLoc.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !down) return;
            down = false;
            Render();
            if (dragging) { dragging = false; if (Moved != null) Moved(Location); return; }
            if (Clicked != null) Clicked(fgAtDown);
        }

        public void KeepOnTop()
        {
            if (Visible) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        public void ShowAt(Point p)
        {
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            Location = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - Width)), Math.Max(wa.Top, Math.Min(p.Y, wa.Bottom - Height)));
            if (!Visible) Show();
            Render();
            KeepOnTop();
        }
    }

    // ------------------------------------------------------------------ MOUSE (per il mini pulsante sulla selezione)
    // Si guarda solo dove si preme e si rilascia il tasto sinistro: niente tastiera, niente testo.
    internal sealed class MouseHook : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT { public Native.POINT pt; public int mouseData, flags, time; public IntPtr extra; }

        public event Action<Point> Down;
        public event Action<Point> Selected;   // trascinamento o doppio clic dentro una finestra che non si è spostata
        private readonly HookProc proc;
        private IntPtr hook;
        private Point downPt, lastUpPt;
        private IntPtr downFg;
        private Native.RECT downRect;
        private int lastUpTime;

        public MouseHook()
        {
            proc = Callback;
            hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0);   // WH_MOUSE_LL
        }

        private IntPtr Callback(int code, IntPtr w, IntPtr l)
        {
            try
            {
                if (code >= 0)
                {
                    int msg = w.ToInt32();
                    if (msg == 0x0201 || msg == 0x0202)
                    {
                        MSLLHOOKSTRUCT s = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(MSLLHOOKSTRUCT));
                        Point p = new Point(s.pt.X, s.pt.Y);
                        if (msg == 0x0201)
                        {
                            downPt = p;
                            downFg = Native.GetForegroundWindow();
                            Native.GetWindowRect(downFg, out downRect);
                            if (Down != null) Down(p);
                        }
                        else
                        {
                            int dx = p.X - downPt.X, dy = p.Y - downPt.Y;
                            bool drag = dx * dx + dy * dy > 144;
                            bool dbl = unchecked(s.time - lastUpTime) < Native.GetDoubleClickTime() &&
                                Math.Abs(p.X - lastUpPt.X) < 6 && Math.Abs(p.Y - lastUpPt.Y) < 6;
                            lastUpTime = s.time;
                            lastUpPt = p;
                            if ((drag || dbl) && Selected != null)
                            {
                                // finestra spostata o ridimensionata: non era una selezione di testo
                                Native.RECT r;
                                IntPtr fg = Native.GetForegroundWindow();
                                Native.GetWindowRect(fg, out r);
                                bool moved = fg == downFg && (r.Left != downRect.Left || r.Top != downRect.Top || r.Right != downRect.Right || r.Bottom != downRect.Bottom);
                                if (!moved) Selected(p);
                            }
                        }
                    }
                }
            }
            catch { }
            return CallNextHookEx(hook, code, w, l);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }
    }

    // ------------------------------------------------------------------ PULSANTI ARROTONDATI (anteprima)
    internal sealed class Pill : Control
    {
        public bool Selected, Primary, Flat;
        private bool hover;

        public Pill(string text, bool primary, bool flat)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Primary = primary;
            Flat = flat;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", primary ? 10f : 9.5f, primary ? FontStyle.Bold : FontStyle.Regular);
            Margin = new Padding(Ui.S(3));
            Text = text;
            MouseEnter += delegate { hover = true; Invalidate(); };
            MouseLeave += delegate { hover = false; Invalidate(); };
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Size sz = TextRenderer.MeasureText(Text, Font);
            Size = new Size(sz.Width + Ui.S(Flat ? 16 : Primary ? 34 : 24), Ui.S(Primary ? 38 : 32));
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, border, text;
            if (Primary)
            {
                fill = !Enabled ? Color.FromArgb(148, 163, 184) : hover ? Color.FromArgb(29, 78, 216) : Ui.Blue;
                border = fill;
                text = Color.White;
            }
            else if (Selected)
            {
                fill = Color.FromArgb(219, 234, 254);
                border = Color.FromArgb(147, 197, 253);
                text = Color.FromArgb(29, 78, 216);
            }
            else
            {
                fill = hover && Enabled ? Ui.Soft : (Parent != null ? Parent.BackColor : Color.White);
                border = Flat ? fill : Ui.Line;
                text = Enabled ? Ui.Ink : Color.FromArgb(148, 163, 184);
            }
            using (System.Drawing.Drawing2D.GraphicsPath p = Ui.Round(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    // ------------------------------------------------------------------ ANTEPRIMA (scegli la modalità, poi Inserisci)
    internal sealed class PreviewForm : Form
    {
        public event Action<string> InsertRequested;
        private readonly Engine engine;
        private readonly Config cfg;
        private readonly string original;
        private string context;
        private string mode;
        private string result = "";
        private int gen;
        private bool working;
        private readonly Dictionary<string, Pill> modeButtons = new Dictionary<string, Pill>();
        private readonly Pill trad;
        private readonly RichTextBox box;
        private readonly Label info, ctxLabel, subtitle;
        private readonly Pill btnInsert, btnAgain, btnCopy, btnCancel;
        private readonly Stopwatch clock = new Stopwatch();

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        public PreviewForm(Engine engine, Config cfg, string original, string context, string mode, Point near)
        {
            this.engine = engine;
            this.cfg = cfg;
            this.original = original ?? "";
            this.context = context ?? "";
            this.mode = mode;
            Text = "Correttore IT";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9.5f);
            Size = new Size(Ui.S(660), Ui.S(430));
            Padding = new Padding(1);

            // intestazione: logo, titolo, chiudi (si trascina da qui)
            Panel head = new Panel();
            head.Dock = DockStyle.Top;
            head.Height = Ui.S(58);
            head.BackColor = Color.White;
            head.Paint += delegate (object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Ui.Badge(e.Graphics, new RectangleF(Ui.S(18), Ui.S(15), Ui.S(28), Ui.S(28)), Ui.Blue, Ui.Violet, false);
            };
            Label title = new Label();
            title.Text = "Correttore IT";
            title.Font = new Font("Segoe UI Semibold", 11.5f);
            title.ForeColor = Ui.Ink;
            title.AutoSize = true;
            title.Location = new Point(Ui.S(56), Ui.S(10));
            subtitle = new Label();
            subtitle.Text = "AI sul tuo PC · il testo non esce dal computer";
            subtitle.Font = new Font("Segoe UI", 8.5f);
            subtitle.ForeColor = Ui.Muted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(Ui.S(57), Ui.S(32));
            Pill close = new Pill("✕", false, true);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.Location = new Point(Width - close.Width - Ui.S(14), Ui.S(13));
            close.Click += delegate { Close(); };
            head.Controls.Add(title);
            head.Controls.Add(subtitle);
            head.Controls.Add(close);
            MouseEventHandler drag = delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);   // trascina come la barra del titolo
            };
            head.MouseDown += drag;
            title.MouseDown += drag;
            subtitle.MouseDown += drag;

            // modalità
            FlowLayoutPanel modes = new FlowLayoutPanel();
            modes.Dock = DockStyle.Top;
            modes.AutoSize = true;
            modes.BackColor = Color.White;
            modes.Padding = new Padding(Ui.S(14), 0, Ui.S(14), Ui.S(8));
            modes.WrapContents = true;
            foreach (string[] m in Modalita.Elenco)
            {
                Pill b = new Pill(m[1], false, false);
                b.Tag = m[0];
                b.Click += delegate (object s, EventArgs e) { Run((string)((Pill)s).Tag, 0); };
                modeButtons[m[0]] = b;
                modes.Controls.Add(b);
            }
            trad = new Pill("in " + Lingue.Nome(cfg.TraduciIn) + "  ▾", false, false);
            trad.Click += delegate
            {
                ContextMenuStrip menu = new ContextMenuStrip();
                menu.ShowImageMargin = false;
                foreach (string[] l in Lingue.Elenco)
                {
                    string code = l[0];
                    ToolStripMenuItem it = new ToolStripMenuItem(l[1], null, delegate
                    {
                        cfg.TraduciIn = code;
                        cfg.Save();
                        trad.Text = "in " + Lingue.Nome(code) + "  ▾";
                        Run("traduci", 0);
                    });
                    it.Checked = code == cfg.TraduciIn;
                    menu.Items.Add(it);
                }
                menu.Show(trad, new Point(0, trad.Height));
            };
            modes.Controls.Add(trad);

            // messaggio a cui si risponde (preso dagli appunti): cliccando si ignora
            ctxLabel = new Label();
            ctxLabel.Dock = DockStyle.Top;
            ctxLabel.AutoSize = false;
            ctxLabel.Height = Ui.S(26);
            ctxLabel.Padding = new Padding(Ui.S(18), Ui.S(4), Ui.S(18), 0);
            ctxLabel.ForeColor = Ui.Muted;
            ctxLabel.BackColor = Color.White;
            ctxLabel.Font = new Font("Segoe UI", 8.75f, FontStyle.Italic);
            ctxLabel.Cursor = Cursors.Hand;
            ctxLabel.Click += delegate
            {
                this.context = "";
                ctxLabel.Visible = false;
                if (this.mode == "rispondi" || this.mode == "completa") Run(this.mode, 0);
            };

            Panel sep1 = new Panel(); sep1.Dock = DockStyle.Top; sep1.Height = 1; sep1.BackColor = Ui.Line;

            box = new RichTextBox();
            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.None;
            box.ReadOnly = true;
            box.BackColor = Color.White;
            box.Font = new Font("Segoe UI", 12f);
            box.DetectUrls = false;
            box.ScrollBars = RichTextBoxScrollBars.Vertical;
            Panel boxPanel = new Panel();
            boxPanel.Dock = DockStyle.Fill;
            boxPanel.BackColor = Color.White;
            boxPanel.Padding = new Padding(Ui.S(20), Ui.S(14), Ui.S(16), Ui.S(10));
            boxPanel.Controls.Add(box);

            // piede: stato a sinistra, pulsanti a destra
            Panel foot = new Panel();
            foot.Dock = DockStyle.Bottom;
            foot.Height = Ui.S(64);
            foot.BackColor = Color.FromArgb(248, 250, 252);
            foot.Paint += delegate (object s, PaintEventArgs e) { using (Pen p = new Pen(Ui.Line)) e.Graphics.DrawLine(p, 0, 0, foot.Width, 0); };
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Right;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.AutoSize = true;
            buttons.WrapContents = false;
            buttons.BackColor = foot.BackColor;
            buttons.Padding = new Padding(0, Ui.S(11), Ui.S(12), 0);
            btnInsert = new Pill("Inserisci  ⏎", true, false);
            btnInsert.Click += delegate { DoInsert(); };
            btnCancel = new Pill("Annulla", false, false);
            btnCancel.Click += delegate { Close(); };
            btnCopy = new Pill("Copia", false, false);
            btnCopy.Click += delegate
            {
                string f = FinalText();
                if (f.Length > 0) { try { Clipboard.SetDataObject(f, true, 10, 50); info.Text = "Copiato negli appunti ✓"; } catch { } }
            };
            btnAgain = new Pill("↻  Un'altra", false, false);
            btnAgain.Click += delegate { Run(this.mode, 0.85); };
            foreach (Pill p in new Pill[] { btnInsert, btnCancel, btnCopy, btnAgain }) { p.Margin = new Padding(Ui.S(4), p.Primary ? 0 : Ui.S(3), Ui.S(4), 0); buttons.Controls.Add(p); }
            info = new Label();
            info.Dock = DockStyle.Fill;
            info.TextAlign = ContentAlignment.MiddleLeft;
            info.Padding = new Padding(Ui.S(18), 0, 0, 0);
            info.ForeColor = Ui.Muted;
            info.Font = new Font("Segoe UI", 9f);
            foot.Controls.Add(info);
            foot.Controls.Add(buttons);

            Controls.Add(boxPanel);
            Controls.Add(sep1);
            Controls.Add(ctxLabel);
            Controls.Add(modes);
            Controls.Add(head);
            Controls.Add(foot);

            Rectangle wa = Screen.FromPoint(near).WorkingArea;
            int x = Math.Min(Math.Max(wa.Left + 8, near.X - Ui.S(40)), wa.Right - Width - 8);
            int y = near.Y + Height > wa.Bottom - 8 ? Math.Max(wa.Top + 8, near.Y - Height - Ui.S(40)) : near.Y;
            Location = new Point(x, y);

            KeyDown += OnKey;
            Shown += delegate { KeepOnScreen(); Activate(); ActiveControl = box; Run(this.mode, 0); };
            FormClosed += delegate { gen++; };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW: ombra attorno alla finestra
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }   // angoli arrotondati (Windows 11)
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Color.FromArgb(203, 213, 225))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        // dopo l'adattamento alla scala dello schermo la finestra può essere più grande: si riporta tutta dentro lo schermo
        private void KeepOnScreen()
        {
            Rectangle wa = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, wa.Width - 16), h = Math.Min(Height, wa.Height - 16);
            if (w != Width || h != Height) Size = new Size(w, h);
            Location = new Point(Math.Max(wa.Left + 8, Math.Min(Left, wa.Right - Width - 8)), Math.Max(wa.Top + 8, Math.Min(Top, wa.Bottom - Height - 8)));
        }

        private void OnKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
            else if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control) { e.SuppressKeyPress = true; DoInsert(); }
        }

        private void DoInsert()
        {
            string f = FinalText();
            if (working || f.Trim().Length == 0) return;
            // si torna al programma mentre questa finestra è ancora in primo piano (Windows lo permette solo così)
            if (InsertRequested != null) InsertRequested(f);
            Close();
        }

        // testo che finisce nel programma
        private string FinalText()
        {
            string r = result ?? "";
            if (mode == "completa") return JoinContinuation(original, r);
            if (mode == "correggi") return r;
            return r.Trim();
        }

        public static string JoinContinuation(string text, string cont)
        {
            cont = (cont ?? "").TrimEnd();
            string t = text.TrimEnd();
            if (t.Length > 0 && cont.TrimStart().StartsWith(t, StringComparison.Ordinal)) cont = cont.TrimStart().Substring(t.Length);   // ha ripetuto il testo
            if (cont.Trim().Length == 0) return text;
            if (text.Length == 0) return cont.Trim();
            bool textSpace = char.IsWhiteSpace(text[text.Length - 1]);
            if (textSpace) return text + cont.TrimStart();
            if (char.IsWhiteSpace(cont[0])) return text + " " + cont.TrimStart();
            if (".,;:!?)…".IndexOf(cont[0]) >= 0) return text + cont;
            return text + " " + cont;
        }

        private void Write(string s, Color c, bool bold)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = c;
            box.SelectionFont = new Font(box.Font, bold ? FontStyle.Bold : FontStyle.Regular);
            box.AppendText(s);
        }

        private void Run(string newMode, double temperature)
        {
            mode = newMode;
            foreach (KeyValuePair<string, Pill> kv in modeButtons) { kv.Value.Selected = kv.Key == mode; kv.Value.Invalidate(); }
            bool hasText = original.Trim().Length > 0;
            ctxLabel.Visible = context.Trim().Length > 0 && (mode == "rispondi" || mode == "completa");
            if (ctxLabel.Visible)
            {
                string c1 = Regex.Replace(context.Trim(), "\\s+", " ");
                ctxLabel.Text = "Rispondo a: “" + (c1.Length > 80 ? c1.Substring(0, 80) + "…" : c1) + "”   (clic qui per ignorarlo)";
            }
            int my = ++gen;
            result = "";
            box.Clear();
            if (!hasText && mode != "rispondi")
            {
                SetWorking(false);
                info.Text = "";
                Write("Nessun testo selezionato: seleziona il testo nel programma e riprova, oppure scegli “Scrivi la risposta”.", Color.FromArgb(185, 28, 28), false);
                btnInsert.Enabled = false;
                return;
            }
            SetWorking(true);
            clock.Restart();
            if (mode == "completa") Write(original.TrimEnd(), Ui.Muted, false);
            else Write("Sto scrivendo…", Ui.Muted, false);
            info.Text = Modalita.Nome(mode) + (mode == "traduci" ? " in " + Lingue.Nome(cfg.TraduciIn) : "") + " in corso…";
            string m0 = mode;
            string ctx = context;
            bool first = true;
            Thread t = new Thread(delegate ()
            {
                string err = null;
                try
                {
                    if (m0 == "correggi")
                    {
                        string r = engine.Correct(original);
                        OnUi(delegate { if (my == gen) { result = r; ShowCorrection(); } });
                    }
                    else
                    {
                        engine.Transform(m0, original, ctx, temperature > 0 ? temperature : (m0 == "rispondi" ? 0.4 : 0.2), delegate (string piece)
                        {
                            if (my != gen) return false;
                            OnUi(delegate
                            {
                                if (my != gen) return;
                                if (first) { first = false; if (m0 != "completa") box.Clear(); }
                                result += piece;
                                if (m0 == "completa")
                                {
                                    string joined = JoinContinuation(original, result);
                                    string shown = joined.Substring(Math.Min(original.TrimEnd().Length, joined.Length));
                                    box.Clear();
                                    Write(original.TrimEnd(), Ui.Muted, false);
                                    Write(shown, Color.FromArgb(21, 128, 61), true);
                                }
                                else Write(piece, Ui.Ink, false);
                            });
                            return true;
                        });
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                OnUi(delegate
                {
                    if (my != gen) return;
                    SetWorking(false);
                    if (err != null) { info.Text = "Errore: " + err; return; }
                    if (FinalText().Trim().Length == 0) { info.Text = "Nessun testo: prova “Un'altra”"; btnInsert.Enabled = false; return; }
                    info.Text = "✓ " + Modalita.Nome(mode) + " · " + (clock.ElapsedMilliseconds / 1000.0).ToString("0.0") + " s";
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private void OnUi(MethodInvoker a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        private void SetWorking(bool on)
        {
            working = on;
            btnInsert.Enabled = !on;
            btnAgain.Enabled = !on;
            btnCopy.Enabled = !on;
        }

        // correzione: le parole cambiate in verde
        private void ShowCorrection()
        {
            box.Clear();
            string pat = "\\s+|[\\p{L}\\p{N}_]+(?:['’][\\p{L}\\p{N}_]+)*['’]?|[^\\s\\p{L}\\p{N}_]";
            List<string> A = new List<string>(), B = new List<string>();
            foreach (Match m in Regex.Matches(original, pat)) A.Add(m.Value);
            foreach (Match m in Regex.Matches(result, pat)) B.Add(m.Value);
            bool[] changed = new bool[B.Count];
            if ((long)A.Count * B.Count <= 4000000)
            {
                int n = A.Count, k = B.Count;
                int[,] L = new int[n + 1, k + 1];
                for (int i = n - 1; i >= 0; i--)
                    for (int j = k - 1; j >= 0; j--)
                        L[i, j] = A[i] == B[j] ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
                for (int j = 0; j < k; j++) changed[j] = true;
                int a = 0, b = 0;
                while (a < n && b < k)
                {
                    if (A[a] == B[b]) { changed[b] = false; a++; b++; }
                    else if (L[a + 1, b] >= L[a, b + 1]) a++;
                    else b++;
                }
            }
            int count = 0;
            for (int j = 0; j < B.Count; j++)
            {
                bool c = changed[j] && B[j].Trim().Length > 0;
                if (c) count++;
                Write(B[j], c ? Color.FromArgb(21, 128, 61) : Ui.Ink, c);
            }
            if (count == 0 && result == original) Write("\n\n✓ Nessun errore trovato", Color.FromArgb(21, 128, 61), false);
        }
    }

    // ------------------------------------------------------------------ MESSAGGIO A COMPARSA
    internal sealed class Osd : Form
    {
        private readonly Label label;
        private readonly System.Windows.Forms.Timer hideTimer;

        public Osd()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 28, 40);
            Padding = new Padding(14, 9, 14, 9);
            label = new Label();
            label.AutoSize = true;
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI", 10f, FontStyle.Regular);
            label.Location = new Point(14, 9);
            label.MaximumSize = new Size(420, 0);
            Controls.Add(label);
            hideTimer = new System.Windows.Forms.Timer();
            hideTimer.Tick += delegate { hideTimer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080 | 0x08000000 | 0x00000008; // TOOLWINDOW | NOACTIVATE | TOPMOST
                return cp;
            }
        }

        public void ShowText(string text, int ms)
        {
            label.Text = text;
            Size = new Size(label.PreferredSize.Width + 28, label.PreferredSize.Height + 18);
            Point p = Cursor.Position;
            Rectangle wa = Screen.FromPoint(p).WorkingArea;
            int x = Math.Min(p.X + 18, wa.Right - Width - 8);
            int y = Math.Min(p.Y + 22, wa.Bottom - Height - 8);
            Location = new Point(Math.Max(wa.Left + 8, x), Math.Max(wa.Top + 8, y));
            if (!Visible) Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // TOPMOST, no move/size/activate
            hideTimer.Stop();
            if (ms > 0) { hideTimer.Interval = ms; hideTimer.Start(); }
        }
    }

    // ------------------------------------------------------------------ SCORCIATOIA GLOBALE
    internal sealed class HotkeyWindow : NativeWindow
    {
        private readonly Action onHotkey;
        private bool registered;
        private const int Id = 0xB17B;

        public HotkeyWindow(Action action)
        {
            onHotkey = action;
            CreateHandle(new CreateParams());
        }

        public bool Register(string spec)
        {
            uint mods = 0x4000; // MOD_NOREPEAT
            Keys key = Keys.None;
            foreach (string part in spec.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 0x2;
                else if (p == "alt") mods |= 0x1;
                else if (p == "shift" || p == "maiusc") mods |= 0x4;
                else if (p == "win") mods |= 0x8;
                else
                {
                    try { key = (Keys)Enum.Parse(typeof(Keys), part.Trim(), true); } catch { key = Keys.None; }
                }
            }
            if (key == Keys.None) return false;
            registered = Native.RegisterHotKey(Handle, Id, mods, (uint)key);
            return registered;
        }

        public void Unregister()
        {
            if (registered) Native.UnregisterHotKey(Handle, Id);
            registered = false;
            DestroyHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == Id) { onHotkey(); return; }
            base.WndProc(ref m);
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder s, int max);
        [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize, flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        // riporta in primo piano il programma dell'utente e controlla che sia davvero lui a ricevere i tasti
        public static bool BringToFront(IntPtr h)
        {
            for (int i = 0; i < 4; i++)
            {
                if (i > 0)
                {
                    // un tasto Alt "vuoto" sblocca il cambio di finestra quando Windows lo impedisce
                    keybd_event(0x12, 0, 0, UIntPtr.Zero);
                    keybd_event(0x12, 0, 0x0002, UIntPtr.Zero);
                }
                SetForegroundWindow(h);
                for (int k = 0; k < 6; k++)
                {
                    Thread.Sleep(25);
                    Application.DoEvents();
                    if (GetForegroundWindow() == h) { Thread.Sleep(60); return true; }
                }
            }
            return false;
        }

        public static bool IsOwnWindow(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }

        public static string ClassOf(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(128);
            GetClassName(h, sb, 128);
            return sb.ToString();
        }

        public static string ProcessNameOf(IntPtr h)
        {
            try
            {
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                using (Process p = Process.GetProcessById((int)pid)) return p.ProcessName.ToLowerInvariant();
            }
            catch { return ""; }
        }

        // punto sotto il cursore di scrittura del programma (se il programma lo comunica), altrimenti il mouse
        public static Point CaretPoint(IntPtr fg)
        {
            try
            {
                uint pid;
                uint tid = GetWindowThreadProcessId(fg, out pid);
                GUITHREADINFO gi = new GUITHREADINFO();
                gi.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));
                if (GetGUIThreadInfo(tid, ref gi) && gi.hwndCaret != IntPtr.Zero)
                {
                    POINT p = new POINT();
                    p.X = gi.rcCaret.Left;
                    p.Y = gi.rcCaret.Bottom;
                    if (ClientToScreen(gi.hwndCaret, ref p)) return new Point(p.X, p.Y + 6);
                }
            }
            catch { }
            return Cursor.Position;
        }
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        public static void WaitModifiersReleased(int timeoutMs)
        {
            int[] keys = new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C };
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                bool down = false;
                foreach (int k in keys) if ((GetAsyncKeyState(k) & 0x8000) != 0) { down = true; break; }
                if (!down) return;
                Thread.Sleep(15);
            }
        }

        public static void SendCtrl(Keys k)
        {
            const uint KEYUP = 0x0002;
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event((byte)k, 0, 0, UIntPtr.Zero);
            keybd_event((byte)k, 0, KEYUP, UIntPtr.Zero);
            keybd_event(0x11, 0, KEYUP, UIntPtr.Zero);
        }

        // frecce come tasti "estesi": altrimenti con BlocNum attivo diventano i numeri del tastierino
        public static void SendArrow(Keys k)
        {
            const uint EXT = 0x0001, KEYUP = 0x0002;
            keybd_event((byte)k, 0, EXT, UIntPtr.Zero);
            keybd_event((byte)k, 0, EXT | KEYUP, UIntPtr.Zero);
        }

        public static void SendShiftArrows(Keys k, int count)
        {
            const uint KEYUP = 0x0002;
            keybd_event(0x10, 0, 0, UIntPtr.Zero);
            for (int i = 0; i < count; i++)
            {
                SendArrow(k);
                if (i % 200 == 199) Thread.Sleep(5);
            }
            keybd_event(0x10, 0, KEYUP, UIntPtr.Zero);
            Thread.Sleep(30);
        }
    }

    // ------------------------------------------------------------------ il motore si chiude sempre insieme al programma
    internal static class JobHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BASIC
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO
        {
            public ulong a, b, c, d, e, f;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EXTENDED
        {
            public BASIC Basic;
            public IO Io;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attr, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int type, ref EXTENDED info, uint len);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        public static IntPtr CreateKillOnCloseJob()
        {
            try
            {
                IntPtr j = CreateJobObject(IntPtr.Zero, null);
                if (j == IntPtr.Zero) return IntPtr.Zero;
                EXTENDED info = new EXTENDED();
                info.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
                SetInformationJobObject(j, 9, ref info, (uint)Marshal.SizeOf(typeof(EXTENDED)));
                return j;
            }
            catch { return IntPtr.Zero; }
        }

        public static void Assign(IntPtr job, Process p)
        {
            if (job == IntPtr.Zero) return;
            try { AssignProcessToJobObject(job, p.Handle); } catch { }
        }
    }
}
