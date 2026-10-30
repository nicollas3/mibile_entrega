#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then bash scripts/init-dev.sh; fi
set -a
source .env
set +a
case "${1:-}" in
 api) export ASPNETCORE_URLS=http://127.0.0.1:5070; exec dotnet run --no-build --project src/Brasa.Api --no-launch-profile ;;
 web) export ASPNETCORE_URLS=http://127.0.0.1:5080; exec dotnet run --no-build --project src/Brasa.Web --no-launch-profile ;;
 *) echo 'Uso: bash scripts/dev.sh api|web' >&2; exit 2 ;;
esac
