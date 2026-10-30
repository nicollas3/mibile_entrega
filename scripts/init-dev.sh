#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -e .env ]]; then echo '.env existente preservado.'; exit 0; fi
umask 077
jwt_value="$(openssl rand -hex 48)"
webhook_value="$(openssl rand -hex 32)"
sed -e "s/__GENERATE_JWT__/$jwt_value/" -e "s/__GENERATE_WEBHOOK__/$webhook_value/" .env.example > .env
echo '.env local criado com segredos aleatórios. Não versione este arquivo.'
