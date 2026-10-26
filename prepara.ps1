# Correttore IT - preparazione della cartella portatile (nessun privilegio di amministratore).
# Tutto resta in questa cartella: niente registro di Windows, niente collegamenti.
# Serve solo la prima volta su un PC: sceglie il motore adatto alla scheda video e scarica i modelli se mancano.
param(
  [ValidateSet('auto', 'leggero', 'qualita')]
  [string]$Modello = 'auto'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
try { $Host.UI.RawUI.WindowTitle = 'Correttore IT - Preparazione' } catch {}

function Step([string]$t) { Write-Host ''; Write-Host "==> $t" -ForegroundColor Cyan }
function Fail([string]$t) {
  Write-Host ''
  Write-Host "ERRORE: $t" -ForegroundColor Red
  Write-Host ''
  Read-Host 'Premi Invio per chiudere'
  exit 1
}

$dir = (Resolve-Path (Split-Path -Parent $MyInvocation.MyCommand.Path)).Path.TrimEnd('\')
Write-Host ''
Write-Host ' Correttore IT - correttore e assistente di scrittura con AI locale (portatile)' -ForegroundColor White
Write-Host " Cartella: $dir"

# ------------------------------------------------------------ 1. preparazione
Step 'Preparazione'
Get-Process CorrettoreIT, llama-server -ErrorAction SilentlyContinue |
  Where-Object { $_.Path -and $_.Path.StartsWith($dir, [StringComparison]::OrdinalIgnoreCase) } |
  Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600
foreach ($d in 'app', 'estensione-chrome') {
  if (-not (Test-Path (Join-Path $dir $d))) { Fail "Cartella '$d' mancante: estrai TUTTO lo zip prima di avviare Prepara.bat." }
}
Get-ChildItem $dir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# ------------------------------------------------------------ 2. analisi del PC
Step 'Analisi del PC'
$arch = 'x64'
if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { $arch = 'arm64' }
$ramGB = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB)
$gpus = @(Get-CimInstance Win32_VideoController | ForEach-Object { $_.Name })
$nvidia = $false
$vramMB = 0
if ($arch -eq 'x64' -and (@($gpus | Where-Object { $_ -match 'NVIDIA' }).Count -gt 0)) {
  $nvidia = $true
  $smi = Join-Path $env:WINDIR 'System32\nvidia-smi.exe'
  if (Test-Path $smi) {
    try {
      $line = & $smi --query-gpu=memory.total --format=csv,noheader,nounits | Select-Object -First 1
      $vramMB = [int]("$line".Trim())
    } catch { $vramMB = 0 }
  }
}
# AMD Radeon / Intel Arc dedicate -> Vulkan (le grafiche integrate restano su CPU: spesso non sono più veloci)
$vulkan = $false
$dedicatedRx = 'Radeon.*(RX|Pro|VII)|Radeon\(TM\) (RX|Pro)|Intel\(R\) Arc\(TM\) [AB]|Intel.*Arc.*[AB][0-9]{3}'
if (-not $nvidia -and $arch -eq 'x64' -and (@($gpus | Where-Object { $_ -match $dedicatedRx }).Count -gt 0)) {
  $vulkan = $true
  # VRAM reale dal registro (Win32_VideoController si ferma a 4 GB)
  try {
    $cls = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
    Get-ChildItem $cls -ErrorAction SilentlyContinue | ForEach-Object {
      $pp = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
      if ($pp -and $pp.DriverDesc -match $dedicatedRx -and $pp.'HardwareInformation.qwMemorySize') {
        $mb = [int]([int64]$pp.'HardwareInformation.qwMemorySize' / 1MB)
        if ($mb -gt $vramMB) { $vramMB = $mb }
      }
    }
  } catch {}
}
Write-Host ("  RAM: {0} GB" -f $ramGB)
Write-Host ("  Scheda grafica: {0}" -f ($gpus -join ', '))
if ($nvidia) { Write-Host ("  NVIDIA rilevata (VRAM: {0} MB): uso l'accelerazione CUDA" -f $vramMB) -ForegroundColor Green }
elseif ($vulkan) { Write-Host ("  Scheda AMD/Intel Arc dedicata (VRAM: {0} MB): uso l'accelerazione Vulkan" -f $vramMB) -ForegroundColor Green }
else { Write-Host '  Nessuna scheda video dedicata: il motore userà il processore (CPU)' }

$models = @{
  leggero = @{
    nome  = 'Qwen3.5 2B'
    file  = 'Qwen3.5-2B-Q4_K_M.gguf'
    url   = 'https://huggingface.co/unsloth/Qwen3.5-2B-GGUF/resolve/main/Qwen3.5-2B-Q4_K_M.gguf'
    minMB = 1200
    peso  = '1,3 GB'
  }
  qualita = @{
    nome  = 'Qwen3.5 4B MTP'
    file  = 'Qwen3.5-4B-Q4_K_M.gguf'
    url   = 'https://huggingface.co/unsloth/Qwen3.5-4B-MTP-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf'
    minMB = 2600
    peso  = '2,8 GB'
    mtp   = $true
  }
}
# modello "Base": scrive continuando il testo come una persona, ideale per il testo grigio
$sug = @{
  nome  = 'Qwen3.5 2B Base (suggerimenti)'
  file  = 'Qwen3.5-2B-Base-Q4_K_M.gguf'
  url   = 'https://huggingface.co/mradermacher/Qwen3.5-2B-Base-GGUF/resolve/main/Qwen3.5-2B-Base.Q4_K_M.gguf'
  minMB = 1200
  peso  = '1,3 GB'
}
if ($Modello -eq 'auto') {
  # 4B: molto più preciso in italiano. Su GPU con almeno 4 GB, oppure su CPU con almeno 12 GB di RAM
  if (((($nvidia -or $vulkan) -and $vramMB -ge 4000)) -or $ramGB -ge 12) { $Modello = 'qualita' } else { $Modello = 'leggero' }
}
$m = $models[$Modello]
$gpuLayers = 0
if ($nvidia -or $vulkan) { $gpuLayers = 99 }
Write-Host ("  Modello: {0} ({1})" -f $m.nome, $m.peso) -ForegroundColor Green

# ------------------------------------------------------------ download (curl di Windows, riprende se si interrompe)
function Download([string]$url, [string]$out, [string]$label, [long]$minBytes) {
  $part = "$out.part"
  $curl = Join-Path $env:WINDIR 'System32\curl.exe'
  $done = $false
  if (Test-Path $curl) {
    $max = 20
    for ($try = 1; $try -le $max -and -not $done; $try++) {
      if ($try -gt 1) {
        $got = 0
        if (Test-Path $part) { $got = [math]::Round((Get-Item $part).Length / 1MB) }
        Write-Host ("  Connessione interrotta, riprendo da {0} MB (tentativo {1} di {2})..." -f $got, $try, $max) -ForegroundColor Yellow
        Start-Sleep -Seconds ([math]::Min(15, 2 * $try))
      }
      $old = $ErrorActionPreference
      $ErrorActionPreference = 'Continue'
      # --http1.1 evita gli errori "schannel: server closed abruptly" di Windows con alcuni CDN
      & $curl -L --fail --http1.1 --connect-timeout 30 --speed-limit 1024 --speed-time 60 -C - --progress-bar -o $part $url
      $code = $LASTEXITCODE
      $ErrorActionPreference = $old
      if ($code -eq 0) { $done = $true }
      elseif ((Test-Path $part) -and ((Get-Item $part).Length -ge $minBytes) -and ($code -eq 22 -or $code -eq 33)) { $done = $true }
      elseif ($code -eq 22 -and (Test-Path $part) -and ((Get-Item $part).Length -lt 1MB)) { break }
    }
  }
  if (-not $done) {
    Write-Host '  Provo con il servizio di download di Windows (BITS)...' -ForegroundColor Yellow
    try {
      if (Test-Path $part) { Remove-Item $part -Force }
      Start-BitsTransfer -Source $url -Destination $part -DisplayName "Correttore IT - $label" -Priority Foreground -ErrorAction Stop
      $done = $true
    } catch {
      throw "download di $label non riuscito. Controlla la connessione e riavvia Prepara.bat: riprenderà da dove si è fermato."
    }
  }
  if ((Get-Item $part).Length -lt $minBytes) { throw "file di $label incompleto: riavvia Prepara.bat." }
  Move-Item $part $out -Force
}

$tmp = Join-Path $dir 'tmp'
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

# ------------------------------------------------------------ 3. motore llama.cpp
$want = 'cpu'
if ($nvidia) { $want = 'cuda' } elseif ($vulkan) { $want = 'vulkan' }
$llamaDir = Join-Path $dir 'llama'
$cfgPath = Join-Path $dir 'config.json'
$prev = $null
if (Test-Path $cfgPath) { try { $prev = Get-Content $cfgPath -Raw | ConvertFrom-Json } catch {} }
$haveServer = Test-Path (Join-Path $llamaDir 'llama-server.exe')
$usedKind = $null
if ($haveServer -and $prev -and $prev.backend -eq $want) {
  Step 'Motore llama.cpp: già pronto per questo PC'
  $usedKind = $want
} else {
  function LocalKind([string]$n) { if ($n -match '-bin-win-cuda-') { 'cuda' } elseif ($n -match '-bin-win-vulkan-') { 'vulkan' } else { 'cpu' } }
  $pk = Join-Path $dir 'motori'
  $local = @()
  if (Test-Path $pk) { $local = @(Get-ChildItem $pk -Filter 'llama-*-bin-win-*.zip' -File -ErrorAction SilentlyContinue) }
  $localMatch = $local | Where-Object { (LocalKind $_.Name) -eq $want } | Select-Object -First 1
  if ($want -eq 'cpu' -and $haveServer) {
    Step 'Motore llama.cpp: uso quello per processore già presente'
    $usedKind = 'cpu'
  } elseif ($localMatch) {
    Step ('Motore llama.cpp per ' + $want.ToUpper() + ': incluso nella cartella (nessun download)')
    $new = Join-Path $tmp 'llama'
    Expand-Archive -Path $localMatch.FullName -DestinationPath $new -Force
    if (Test-Path $llamaDir) { Remove-Item $llamaDir -Recurse -Force }
    Move-Item $new $llamaDir
    $usedKind = $want
  } else {
    Step 'Download del motore llama.cpp (versione ufficiale verificata)'
    $tag = 'b11541'
    $base = "https://github.com/ggml-org/llama.cpp/releases/download/$tag/"
    $files = @("llama-$tag-bin-win-cpu-$arch.zip")
    if ($nvidia) { $files = @("llama-$tag-bin-win-cuda-12.4-x64.zip", 'cudart-llama-bin-win-cuda-12.4-x64.zip') }
    elseif ($vulkan) { $files = @("llama-$tag-bin-win-vulkan-x64.zip") }
    try {
      $new = Join-Path $tmp 'llama'
      foreach ($f in $files) {
        $z = Join-Path $tmp $f
        Download ($base + $f) $z 'llama.cpp' 1MB
        Expand-Archive -Path $z -DestinationPath $new -Force
      }
      if (Test-Path $llamaDir) { Remove-Item $llamaDir -Recurse -Force }
      Move-Item $new $llamaDir
      $usedKind = $want
    } catch {
      if ($haveServer) { Write-Host '  Download non riuscito: resta il motore per processore.' -ForegroundColor Yellow; $usedKind = 'cpu' }
      else { Fail $_.Exception.Message }
    }
  }
  # solo il server serve: gli altri strumenti di llama.cpp occupano spazio inutilmente
  Get-ChildItem $llamaDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^(llama-(?!server)[^.]*\.(exe|dll)|llama\.exe|ggml-rpc.*)$' -and $_.Name -notmatch '^llama-common\.dll$' } |
    Remove-Item -Force -ErrorAction SilentlyContinue
}
if ($usedKind -ne $want -or $usedKind -eq 'cpu') { $gpuLayers = 0 }
if (-not (Test-Path (Join-Path $llamaDir 'llama-server.exe'))) { Fail 'llama-server.exe non trovato.' }

# ------------------------------------------------------------ 4. modelli AI
$modelsDir = Join-Path $dir 'models'
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
function EnsureModel($mm) {
  $p = Join-Path $modelsDir $mm.file
  if ((Test-Path $p) -and ((Get-Item $p).Length -ge ($mm.minMB * 1MB))) { Write-Host ("  {0}: già presente" -f $mm.nome); return }
  Write-Host ("  {0} ({1}): si scarica solo la prima volta..." -f $mm.nome, $mm.peso)
  Download $mm.url $p $mm.nome ($mm.minMB * 1MB)
}
Step 'Modelli AI'
# se nella cartella c'è già il modello grande (copia da un altro PC) lo si usa anche qui
if ($Modello -eq 'leggero' -and (Test-Path (Join-Path $modelsDir $models.qualita.file))) { $m = $models.qualita }
try { EnsureModel $m } catch { Fail $_.Exception.Message }
$sugFile = ''
if ($m.file -ne $models.leggero.file -and ($ramGB -ge 12 -or $gpuLayers -gt 0)) {
  try { EnsureModel $sug; $sugFile = $sug.file; Write-Host '  Testo grigio: modello dedicato, veloce e pertinente' -ForegroundColor Green }
  catch { Write-Host '  Modello dei suggerimenti non disponibile: si usa quello principale.' -ForegroundColor Yellow }
}

# ------------------------------------------------------------ 5. programma (compilato sul posto con .NET di Windows, se serve)
$exe = Join-Path $dir 'CorrettoreIT.exe'
$cs = Join-Path $dir 'app\CorrettoreIT.cs'
if (-not (Test-Path $exe) -or ((Get-Item $cs).LastWriteTime -gt (Get-Item $exe).LastWriteTime)) {
  Step 'Creazione del programma'
  $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
  if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
  if (-not (Test-Path $csc)) { Fail '.NET Framework 4 non trovato (è incluso in Windows 10 e 11).' }
  if (Test-Path $exe) { Remove-Item $exe -Force }
  $old = $ErrorActionPreference
  $ErrorActionPreference = 'Continue'
  $ico = Join-Path $dir 'app\icona.ico'
  $out = & $csc /nologo /codepage:65001 /target:winexe /optimize+ "/out:$exe" "/win32icon:$ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $cs 2>&1
  $code = $LASTEXITCODE
  $ErrorActionPreference = $old
  if ($code -ne 0 -or -not (Test-Path $exe)) { $out | ForEach-Object { Write-Host $_ }; Fail 'Creazione del programma non riuscita.' }
}

# ------------------------------------------------------------ 6. impostazioni (nella cartella)
# su processore al massimo 8 thread (oltre, i core "efficienti" rallentano il motore)
$threads = 0
$idle = 600
if ($gpuLayers -eq 0) {
  $cores = 0
  try { $cores = [int](Get-CimInstance Win32_Processor | Measure-Object -Property NumberOfCores -Sum).Sum } catch {}
  if ($cores -gt 0) { $threads = [math]::Min(8, $cores) }
  if ($ramGB -ge 16) { $idle = 0 }
}
$cfg = [ordered]@{
  porta               = 47821
  portaMotore         = 47822
  modello             = "models\$($m.file)"
  modelloSuggerimenti = $(if ($sugFile) { "models\$sugFile" } else { '' })
  nomeModello         = $m.nome
  gpuLayers           = $gpuLayers
  threads             = $threads
  contesto            = 4096
  spegniDopoSecondi   = $idle
  scorciatoia         = 'Ctrl+Shift+Space'
  attivo              = $true
  mtp                 = [bool]$m.mtp
  backend             = $usedKind
  tempMaxGpu          = 80
  pausaDopoSecondi    = 45
}
# si tengono le scelte dell'utente (scorciatoia, lingue, pulsante e sua posizione)
if ($prev) {
  if ($prev.scorciatoia -and $prev.scorciatoia -notmatch '(^|\+)\s*alt\s*(\+|$)') { $cfg.scorciatoia = $prev.scorciatoia }
  foreach ($k in 'lingua', 'traduciIn', 'pulsanteGalleggiante', 'pulsanteX', 'pulsanteY', 'miniPulsanteSelezione') {
    if ($null -ne $prev.$k) { $cfg[$k] = $prev.$k }
  }
}
[IO.File]::WriteAllText($cfgPath, ($cfg | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
# niente file inutili: si tengono solo i modelli in uso
Get-ChildItem $modelsDir -File | Where-Object { $_.Name -ne $m.file -and $_.Name -ne $sugFile } | Remove-Item -Force -ErrorAction SilentlyContinue
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

Start-Process -FilePath $exe -ArgumentList '--primo-avvio' -WorkingDirectory $dir

# ------------------------------------------------------------ 7. Chrome / Edge (unico passo manuale)
$ext = Join-Path $dir 'estensione-chrome'
$size = [math]::Round(((Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum) / 1GB, 2)
Write-Host ''
Write-Host ' PRONTO' -ForegroundColor Green
Write-Host (" Spazio occupato: {0} GB, tutto in questa cartella." -f $size)
Write-Host ' Da ora basta avviare CorrettoreIT.exe. Avvio automatico con Windows: tasto destro sull''icona.'
Write-Host ''
Write-Host ' Windows: seleziona un testo in qualsiasi programma e premi Ctrl+Maiusc+Spazio.'
Write-Host ''
try { Set-Clipboard -Value $ext } catch {}
Write-Host ' Chrome o Edge - una volta sola (il browser lo chiede per sicurezza):' -ForegroundColor Yellow
Write-Host '   1. apri chrome://extensions  ->  attiva "Modalità sviluppatore"'
Write-Host '   2. clicca "Carica estensione non pacchettizzata"'
Write-Host '   3. incolla il percorso con Ctrl+V e scegli la cartella:'
Write-Host "      $ext"
Write-Host '   (se avevi già l''estensione da un''altra cartella, rimuovi prima quella vecchia)'
Write-Host ''
Read-Host 'Premi Invio per chiudere'
