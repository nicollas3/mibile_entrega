param([string]$ApiUrl = 'http://10.0.2.2:5070')
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$uri = $null
if (-not [Uri]::TryCreate($ApiUrl, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -notin @('http', 'https')) { throw 'Informe uma URL HTTP/HTTPS valida para a API.' }
dotnet build src/Brasa.Mobile/Brasa.Mobile.csproj -c Debug -f net10.0-android "-p:BrasaApiUrl=$ApiUrl" -p:RuntimeIdentifier=android-arm64
if ($LASTEXITCODE -ne 0) { throw 'Falha no build. Confira o workload MAUI, Android SDK e JDK no Visual Studio Installer.' }
$apk = 'src/Brasa.Mobile/bin/Debug/net10.0-android/android-arm64/br.com.brasa.delivery-Signed.apk'
if (-not (Test-Path $apk)) { throw 'O build nao produziu o APK no caminho esperado.' }
New-Item -ItemType Directory -Force artifacts | Out-Null
Copy-Item $apk artifacts/Brasa-dev.apk -Force
Write-Host ('APK de desenvolvimento: ' + (Join-Path $PWD 'artifacts/Brasa-dev.apk'))
Write-Host 'Mantenha a API ligada. Este APK usa pagamentos simulados e assinatura de depuracao.'
