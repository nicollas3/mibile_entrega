# Brasa — comida, pessoas e boas conexões

Plataforma original de delivery em C#/.NET, com API, PostgreSQL, painéis Blazor e aplicativo MAUI para cliente e entregador. A identidade visual é própria. O projeto funciona em **desenvolvimento**, com pagamentos fictícios claramente identificados. Nenhuma cobrança ou repasse bancário é realizado.

## Começar em cinco minutos

Pré-requisitos para API/web: .NET SDK **10.0.403**, Docker com Compose v2, Bash e OpenSSL. No Windows, use WSL2 para os scripts ou configure as mesmas variáveis no PowerShell. A compilação mobile tem pré-requisitos próprios abaixo.

Nesta máquina em nuvem, o SDK já está em `/workspace/.dotnet`; antes dos comandos abaixo, execute `export PATH="/workspace/.dotnet:$PATH"`. Em uma instalação convencional, use o `dotnet` instalado no sistema.

Na raiz do repositório:

```bash
bash scripts/init-dev.sh
docker compose up -d postgres
dotnet tool restore
dotnet restore Brasa.slnx --locked-mode
dotnet build Brasa.slnx --no-restore
```

Em dois terminais, na raiz:

```bash
bash scripts/dev.sh api
```

```bash
bash scripts/dev.sh web
```

A API escuta na porta **5070** e os painéis na **5080**, ambas somente na máquina local. A interface Swagger fica em `/docs`; o documento é `/openapi/v1.json`; a prontidão consulta o banco em `/health`. As migrations são aplicadas ao iniciar quando `Database__Migrate=true`. Os scripts preservam um `.env` existente e nunca imprimem seus segredos. Recompile após editar o código: os scripts iniciam os binários compilados com `--no-build`.

Também é possível executar os três serviços com Compose:

```bash
bash scripts/init-dev.sh
docker compose up --build -d
docker compose logs --tail=50 api web
```

Não inicie o PostgreSQL do Compose se já houver outro serviço usando a porta 5432. `docker compose down` preserva o volume; **`down -v` apaga os dados locais**.

## Contas e dados fictícios

O seed exige simultaneamente `ASPNETCORE_ENVIRONMENT=Development`, `Demo__Seed=true` e `Demo__Password`. A configuração de exemplo usa a senha **`BrasaLocal!2026`**, exclusivamente local. O seed é repetível e não redefine senhas nem sobrescreve dados existentes.

| Email local | Perfil |
|---|---|
| `cliente@brasa.local` | Cliente com endereço fictício |
| `outro@brasa.local` | Segundo cliente |
| `loja@brasa.local` | Gestor da Cozinha Aurora |
| `loja2@brasa.local` | Gestor do Verde no Pote, isolado da primeira loja |
| `funcionario@brasa.local` | Funcionário da primeira loja, apenas pedidos |
| `entregador@brasa.local`, `entregador2@brasa.local` | Entregadores fictícios aprovados |
| `admin@brasa.local` | Administração local |
| `suporte@brasa.local` | Atendimento, sem acesso financeiro ou gestão de contas |

Há duas lojas, produtos, adicionais, alérgenos declarados, pedidos em estados distintos e o cupom `BEMVINDO` (R$ 8 de desconto, subtotal mínimo R$ 25, validade de três meses a partir da criação). Endereços e coordenadas são fictícios. Nenhuma localização de entregador é gerada pelo seed.

Cadastro público permite somente cliente ou entregador pendente de aprovação. A administração aprova entregadores, atribui perfis e vincula contas às lojas. Para remover o acesso de um funcionário à loja, atribua zero permissões. Bloqueio de conta revoga suas sessões. Dados demonstrativos podem ser desativados com `Demo__Seed=false`; a configuração operacional básica e os papéis continuam sendo inicializados.

## Como experimentar um pedido completo

1. Entre como cliente, explore uma loja, escolha adicionais e adicione itens. O carrinho é persistido no PostgreSQL. É preciso esvaziá-lo explicitamente antes de trocar de restaurante.
2. Abra o carrinho, selecione um endereço dentro da área, confira subtotal/desconto/entrega/serviço/total e confirme com `demo-approved`. O preço é calculado pelo servidor e preservado no pedido.
3. Em outra sessão de navegador, entre como `loja`, aceite o pedido, inicie o preparo e marque como pronto. A equipe só enxerga e altera a loja para a qual tem permissão.
4. No aplicativo, entre como entregador aprovado, fique disponível e aceite a oferta antes do prazo. As ofertas indicam distância em linha reta, destino aproximado e valor previsto; o endereço exato só aparece após o aceite.
5. Confirme ida e chegada ao restaurante. No painel da loja, confirme a retirada pelo entregador presente. Só então ele poderá marcar como retirado, ir ao cliente e confirmar a entrega.
6. Opcionalmente, o entregador autoriza GPS durante a tela da entrega. O cliente acompanha etapas e a última posição informada. Sem GPS ou rede, a interface informa a indisponibilidade e permite tentar novamente. Após entregar, o servidor rejeita novas posições e apaga a última posição armazenada.
7. O cliente avalia uma única vez após a entrega. Cancelamentos depois do início do preparo precisam de suporte. A administração pode cancelar com motivo e processar reembolsos fictícios, parciais ou totais, sem ultrapassar o total pago.

O painel administrativo permite relatórios por período/loja/status/método/região, contas, entregadores, cupons de campanhas, taxa de serviço, comissões, prazos, lojas e auditoria. Categorias de pratos e restaurantes são textos gerenciados nos formulários dos respectivos cadastros; não há uma taxonomia global hierárquica. Promoções são cupons de valor fixo, sem motor de segmentação ou anúncios patrocinados.

## Solução e versões

| Componente | Versão escolhida |
|---|---|
| .NET SDK / C# | 10.0.403 / compilador do SDK |
| ASP.NET Core, Identity, EF Core, SignalR | 10.0.14, LTS .NET 10 |
| Npgsql EF PostgreSQL | 10.0.3 (linha EF Core 10) |
| PostgreSQL | 17.6 Alpine, imagem fixada por digest |
| .NET MAUI Controls | 10.0.120, para .NET 10 |
| Workload MAUI Android / Android SDK .NET | 10.0.0 / 36.1.2 |
| Android API / build-tools | 36 / 36.0.0 |
| Swagger UI | Swashbuckle 10.3.1; geração pelo OpenAPI do ASP.NET |
| Testes | xUnit 2.9.3, adapter 3.1.5, Test SDK 18.11.0 |

As versões foram conferidas nos [metadados oficiais do .NET 10](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) e no [NuGet](https://api.nuget.org/v3/index.json). `global.json`, `Directory.Packages.props`, `dotnet-tools.json` e os `packages*.lock.json` registram as escolhas. Lockfiles específicos por RuntimeIdentifier evitam que builds Android alterem o lockfile portátil usado pela API/web. Evite atualizar EF, Identity e ASP.NET separadamente para outra versão principal. MAUI tem seu próprio ciclo de suporte, distinto do LTS do servidor; revise workloads/Xcode antes da distribuição.

```text
Brasa.slnx                    API, domínio, persistência, contratos, cliente, web, testes
Brasa.Mobile.slnx             aplicativo MAUI e bibliotecas compartilhadas
src/Brasa.Domain              entidades, máquina de estados e cálculo de valores
src/Brasa.Contracts           DTOs, validações de entrada e apresentação pt-BR
src/Brasa.Infrastructure      casos de uso, Identity/EF, autorização por recurso, integrações
src/Brasa.Api                 composição HTTP/SignalR, autenticação, worker, OpenAPI, migrations
src/Brasa.Client              cliente HTTP, rotação de sessão e comunicação SignalR
src/Brasa.Web                 painéis e fluxo web de cliente para desenvolvimento
src/Brasa.Mobile              áreas MAUI separadas de cliente e entregador
tests/Brasa.Tests             regras e integração com PostgreSQL real
docs/                         arquitetura, configuração e evidências de validação
```

Os casos de uso estão em serviços separados da interface. A API expõe DTOs; entidades EF e credenciais nunca são retornadas diretamente. A solução de servidor não inclui o executável MAUI, permitindo build/testes Linux sem instalar workloads mobile.

## Banco e migrations

```bash
dotnet tool restore
# A fábrica de design usa o banco local padrão; exporte a variável abaixo para outro banco.
export ConnectionStrings__Database='Host=localhost;Database=brasa;Username=brasa;Password=local-development-only'
dotnet ef database update --project src/Brasa.Api
dotnet ef migrations has-pending-model-changes --project src/Brasa.Api
```

As migrations ficam na API, configurada explicitamente como assembly de migrations. Não é usado `EnsureCreated`. Para gerar uma nova migration após uma mudança de modelo, use `dotnet ef migrations add Nome --project src/Brasa.Api --output-dir Migrations` e revise o SQL. Em ambientes publicados, migrations precisam de execução controlada, cópia de segurança e usuário de banco com privilégios mínimos.

## Compilar o aplicativo MAUI

Use JDK **21 completo** (com `java`, `javac`, `jar` e `jarsigner`), Android SDK e workload MAUI Android. Um JRE sozinho não compila APKs. Android mínimo 23, iOS mínimo 15. A área de entregador é separada da área de cliente e exige o papel validado no servidor.

```bash
dotnet workload install maui-android
dotnet build src/Brasa.Mobile -f net10.0-android \
  -p:AndroidSdkDirectory=/caminho/Android/sdk \
  -p:JavaSdkDirectory=/caminho/jdk-21 \
  -p:RuntimeIdentifier=android-x64
```

O modo Debug usa a API no host do emulador Android (`10.0.2.2:5070`) e no host do simulador iOS (`localhost:5070`). Para aparelho físico, informe `-p:BrasaApiUrl=https://seu-endereco-da-api`. O código não ignora erros de certificado. HTTP só é habilitado no manifesto Android de Debug. A compilação Release exige endereço de API explícito e o aplicativo exige HTTPS.

Nesta nuvem, o SDK mobile limpo e seu workload ficam separados. O comando já exercitado aqui é:

```bash
/workspace/.dotnet-mobile/dotnet build src/Brasa.Mobile -f net10.0-android \
  -p:AndroidSdkDirectory=/workspace/android-sdk \
  -p:JavaSdkDirectory=/workspace/toolchains/jdk-root/usr/lib/jvm/java-21-openjdk-amd64 \
  -p:RuntimeIdentifier=android-x64
```

O artefato local `artifacts/Brasa-dev.apk`, quando presente, é assinado para depuração e contém as ABIs ARM64 e x64. Ele aponta para a API do host do emulador; não é uma distribuição de loja nem foi homologado em aparelho.

Para Android ARM64, compile com `-p:RuntimeIdentifier=android-arm64`. Para instalar no emulador conectado, utilize `dotnet build ... -t:Run`. Distribuição exige keystore de assinatura próprio e configuração da loja; o certificado de depuração não é adequado.

**iOS exige macOS, Xcode compatível com o workload e, para aparelho/loja, provisionamento Apple.** Este Linux não compila nem executa iOS:

```bash
# Em macOS com Xcode configurado
dotnet workload install maui-ios maui-android
dotnet build src/Brasa.Mobile -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
# Exemplo de configuração de publicação; exige provedores reais e revisão prévia do servidor
dotnet publish src/Brasa.Mobile -f net10.0-ios -c Release \
  -p:RuntimeIdentifier=ios-arm64 -p:BrasaApiUrl=https://seu-endereco-da-api
```

Tokens são guardados com `SecureStorage` no dispositivo. No web, são mantidos no servidor e persistidos entre recargas em um envelope criptografado por Data Protection no sessionStorage; não são gravados como texto aberto no armazenamento do navegador. Preserve e proteja as chaves Data Protection ao hospedar múltiplas instâncias ou reiniciar containers.

## Testes e verificações

Com PostgreSQL local disponível:

```bash
bash scripts/check.sh
```

Os testes criam um banco `brasa_test_<uuid>` por rodada, aplicam migrations, executam os fluxos HTTP/SignalR e removem **somente esse banco** no encerramento. O usuário de teste precisa de permissão `CREATEDB`. A variável opcional `BRASA_TEST_DATABASE` permite escolher o servidor de teste. Nunca aponte essa variável para um servidor de produção.

Cobertura: valores e arredondamento; transições inválidas/duplicadas; isolamento de lojas e entregadores; permissões de funcionário/suporte; múltiplos restaurantes no carrinho; validação de entradas; checkout concorrente e repetido; snapshot de preços; aprovação/recusa/webhook; retirada confirmada pela loja; entrega completa; GPS após conclusão; avaliações; reembolso parcial idempotente; recuperação de senha; rotação/reutilização/revogação de tokens; bloqueio; expiração; loja indisponível após pagamento; assinatura de grupos SignalR.

A CI executa build Release, testes e checagem de migrations em PostgreSQL. Resultados concretos e limitações da máquina utilizada estão em [docs/VALIDACAO.md](docs/VALIDACAO.md). A CI só é executada no GitHub após você enviar o código; não houve push ou publicação automática.

## O que é real, demonstrativo e externo

**Implementado localmente:** persistência PostgreSQL, Identity com senhas hash, papéis e permissões por loja/recurso, carrinho, checkout, auditoria, estados, ofertas/entrega, posições recebidas de GPS, SignalR, histórico/notificações internas persistentes, suporte, avaliações, relatórios e painéis interativos.

**Demonstração explícita:** contas, restaurantes e pedidos do seed; provedor de pagamento; reembolso; previsões financeiras. Os valores previstos não representam liquidação ou conciliação com uma instituição financeira. O cálculo de área usa distância em linha reta; tempo de preparo é uma estimativa informada pela loja, sem tráfego ou roteirização real. Funcionamento da loja é controlado manualmente pelo status; horários cadastrados são informativos nesta versão.

**Exige implementação/configuração externa:** pagamentos e webhooks de um PSP real, email de recuperação em produção, geocodificação automática, ETA por rota/tráfego, push FCM/APNs e rastreamento em segundo plano. As interfaces `IPaymentProvider`, `IAccountEmail`, `IGeocoder`, `IMapsService` e `IPushNotifier` isolam esses pontos. `DevelopmentPush` não envia push ao sistema operacional; os avisos internos e SignalR funcionam. O app abre rotas no serviço de mapas instalado no aparelho e permite conferir coordenadas, sem incorporar um mapa licenciado próprio.

O GPS funciona em primeiro plano e para quando a tela sai de foco; não há serviço Android em segundo plano nem entitlement iOS de background location. Não há fila offline de atualizações de etapas: falhas são informadas e exigem nova tentativa. O checkout mantém sua chave para repetir uma solicitação após timeout.

Esta entrega não está homologada para produção. O servidor **recusa iniciar fora de Development** enquanto apenas integrações demonstrativas estiverem registradas. Isso evita habilitar cobranças, recuperação de senha ou notificações fictícias em uma publicação real. Consulte [docs/OPERACAO.md](docs/OPERACAO.md) para variáveis, regras financeiras, webhook, segurança e condições de publicação.
