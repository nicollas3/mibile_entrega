#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore Brasa.slnx --locked-mode
dotnet build Brasa.slnx --no-restore -c Release
dotnet test tests/Brasa.Tests --no-build -c Release --logger 'trx;LogFileName=brasa.trx'
dotnet ef migrations has-pending-model-changes --project src/Brasa.Api
