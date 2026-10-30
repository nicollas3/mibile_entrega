param([string]$NotebookIp = '', [switch]$SkipDatabase)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Set-Location (Split-Path $PSScriptRoot -Parent)
if ($NotebookIp) {
    $parsedIp = $null
    if (-not [System.Net.IPAddress]::TryParse($NotebookIp, [ref]$parsedIp) -or $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { throw 'Informe o IPv4 do notebook, obtido com ipconfig.' }
}
function New-LocalSecret([int]$Length) {
    $bytes = New-Object byte[] $Length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    return ([BitConverter]::ToString($bytes)).Replace('-', '').ToLowerInvariant()
}
if (-not (Test-Path '.env')) {
    $text = [System.IO.File]::ReadAllText((Join-Path $PWD '.env.example'))
    $text = $text.Replace('__GENERATE_JWT__', (New-LocalSecret 48)).Replace('__GENERATE_WEBHOOK__', (New-LocalSecret 32))
    [System.IO.File]::WriteAllText((Join-Path $PWD '.env'), $text)
}
$settings = @{}
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $PWD '.env'))) {
    if ($line -match '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
        $name = $Matches[1].Replace('__', ':')
        $value = $Matches[2].Trim()
        if ($value.Length -ge 2 -and (($value.StartsWith("'") -and $value.EndsWith("'")) -or ($value.StartsWith('"') -and $value.EndsWith('"')))) { $value = $value.Substring(1, $value.Length - 2) }
        $settings[$name] = $value
    }
}
$settings['AllowedHosts'] = 'localhost;127.0.0.1;10.0.2.2'
if ($NotebookIp) { $settings['AllowedHosts'] += ';' + $NotebookIp }
$settings | ConvertTo-Json -Compress | dotnet user-secrets set --project src/Brasa.Api
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar User Secrets. Confira o SDK .NET 10.' }
if ($NotebookIp) {
    $localProps = Join-Path $PWD 'Directory.Build.local.props'
    if (Test-Path $localProps) { Write-Host 'Directory.Build.local.props existente preservado; confira BrasaApiUrl nesse arquivo.' }
    else { [System.IO.File]::WriteAllText($localProps, "<Project><PropertyGroup><BrasaApiUrl>http://${NotebookIp}:5070</BrasaApiUrl></PropertyGroup></Project>") }
}
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar ferramentas.' }
dotnet restore Brasa.slnx --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar dependencias.' }
if (-not $SkipDatabase) {
    docker compose up -d --wait postgres
    if ($LASTEXITCODE -ne 0) { throw 'Abra o Docker Desktop com containers Linux e repita o comando.' }
}
Write-Host 'Configuracao concluida. Abra Brasa.slnx no Visual Studio e inicie Brasa.Api e Brasa.Web.'
Write-Host 'Segredos locais e endereco particular nao serao enviados ao Git.'
