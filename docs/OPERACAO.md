# Operação, integrações e publicação

## Configuração

| Variável | Finalidade |
|---|---|
| `ConnectionStrings__Database` | Conexão PostgreSQL; deve ficar fora do código |
| `Jwt__Key` | Material aleatório de assinatura, mínimo 32 bytes; gerado pelo init local |
| `Payments__WebhookSecret` | HMAC do webhook **de desenvolvimento**, não é uma chave de PSP |
| `Demo__Seed` | Habilita contas/dados fictícios somente em Development |
| `Demo__Password` | Senha para novas contas fictícias; não modifica contas já criadas |
| `Database__Migrate` | Aplica migrations no startup; usar execução controlada em publicação futura |
| `Api__BaseUrl` | URL usada pelo servidor Blazor para acessar a API |
| `Cors__Origins__0` | Origem web explicitamente permitida; adicione índices para outras origens necessárias |
| `ASPNETCORE_ENVIRONMENT` | Esta versão exige `Development` e não deve ser exposta publicamente |
| `RateLimits__AuthPerMinute` | Limite por IP de autenticação; padrão 25/min; testes isolados usam 1000/min |
| `BRASA_TEST_DATABASE` | Servidor PostgreSQL de testes; precisa permitir criação de bancos |

Não guarde `.env`, tokens, certificados de assinatura de aplicativos ou senhas de produção no Git. O Compose é apenas desenvolvimento e publica portas no loopback. O método de autenticação é bearer, sem cookies de autenticação da API. O aplicativo Release aceita somente endpoint HTTPS; não há callback que desabilite TLS.

## Pagamentos demonstrativos e webhook

`demo-approved` aprova imediatamente; `demo-declined` recusa; `demo-pending` aguarda webhook. Os três são fictícios. Não há campos para PAN, CVV ou dados completos de cartão. O webhook não aceita valores financeiros vindos do cliente: opera sobre os valores já preservados no pedido.

Contrato de demonstração:

```json
{"eventId":"identificador-unico-do-evento","orderId":"UUID-do-pedido-pendente","outcome":"approved"}
```

Envie o JSON exato como UTF-8 para `POST /api/payments/development/webhook` com `Content-Type: application/json`. `X-Brasa-Signature` é o HMAC-SHA256 hexadecimal dos **bytes exatos** desse corpo, usando `Payments__WebhookSecret`. Também é aceito `outcome=declined`. A comparação de assinatura tem tempo constante. O ID do evento é único; o mesmo ID com outro conteúdo é recusado. Essa assinatura não é o contrato de nenhum provedor financeiro real.

No checkout, gere uma chave UUID para `Idempotency-Key` e **reutilize a mesma chave e corpo após timeout**. Uma chave identifica uma tentativa de checkout por cliente. Alterar endereço, cupom ou método exige uma nova chave. O servidor limpa o carrinho na mesma transação do pedido, sem criar outro pedido em uma repetição idêntica. O app mantém a chave em armazenamento seguro até receber a resposta. Reembolsos exigem uma chave própria por operação.

## Valores e relatórios

Todos os valores usam `decimal`, duas casas e arredondamento `AwayFromZero`. Itens/adicionais/quantidades são validados no servidor. Subtotal é calculado sobre os preços atuais ao finalizar; o snapshot passa a ser imutável depois da compra. Alterações de adicionais invalidam seleções antigas no carrinho e exigem revisão.

```text
líquido dos produtos = subtotal - desconto
total = líquido dos produtos + entrega + taxa de serviço
comissão = arredondar(líquido dos produtos × comissão da loja)
restaurante = líquido dos produtos - comissão
entregador previsto = taxa de entrega
plataforma = comissão + taxa de serviço
```

Cupons desta versão são financiados pela redução do valor dos produtos. O total se equilibra com as parcelas acima. Relatórios mostram volume pago e reembolsos separadamente; parcelas líquidas após reembolso parcial são estimadas proporcionalmente. Não existe livro contábil regulatório, conciliação bancária, transferência real, split do PSP ou processamento de chargebacks. Não use esses relatórios para liquidar repasses reais sem definir e implementar tais contratos.

Cancelamento/rejeição de pedido pago registra `RefundStatus=Pending`. A administração processa explicitamente o reembolso demonstrativo, com motivo e auditoria. O pedido não é apagado e seu histórico é preservado. Pedidos entregues incorretamente geram ocorrência para investigação; o sistema não desfaz silenciosamente uma entrega finalizada. Problemas na corrida não permitem continuar etapas automaticamente; o suporte precisa analisar o caso e a administração pode cancelar quando necessário.

O worker verifica os prazos a cada cinco segundos. Pagamento pendente expira em 30 minutos. Resposta da loja, espera por entregador e validade de oferta são configuráveis. Uma oferta recusada ou expirada não é reenviada automaticamente ao mesmo entregador; novos entregadores disponíveis podem recebê-la, e o pedido pode expirar por falta de aceite. A estratégia de distribuição é simples, sem otimização geográfica da frota.

## Contas, privacidade e localização

Identity armazena hashes de senha e aplica bloqueio temporário após cinco falhas. Refresh tokens não são armazenados em texto aberto no banco. Não são aceitos papéis administrativos no cadastro público. Permissões por estabelecimento são verificadas na API; esconder controles na interface é apenas apresentação.

A recuperação local grava uma mensagem em `src/Brasa.Api/.local/mail` (ou `/app/.local/mail` no container). Ela contém um token sensível de recuperação e nunca deve ser publicada ou versionada. O arquivo é uma caixa de email de desenvolvimento; não há email real enviado. Em produção, substitua `IAccountEmail` por um serviço transacional e imponha políticas de retenção/acesso.

Há exportação autenticada de perfil, endereços, pedidos próprios e ocorrências, limitada a 1000 registros de pedidos/ocorrências por resposta. Solicitações maiores, correção e exclusão entram pelo suporte, na categoria de privacidade. Exclusão exige análise de dados com obrigação de retenção; não há apagamento automático dos registros financeiros. Registre a base legal, a política de retenção e o contato de privacidade antes de operar com pessoas reais.

O app só solicita localização para preencher um endereço sob ação explícita ou compartilhar GPS em uma entrega ativa. Não há permissão de localização em segundo plano. O servidor exige entregador atribuído, pedido ativo, coordenadas válidas e horário recente/crescente. Ao concluir ou cancelar, apaga a última posição e rejeita novas atualizações. Registros de pedidos e ocorrências seguem sujeitos à política de retenção.

## Condições antes de publicar uma operação real

O startup atualmente bloqueia ambientes fora de Development. Remover esse bloqueio isoladamente não torna o sistema adequado para produção. É necessário implementar e validar os provedores reais, seus webhooks, a conciliação/reconciliação financeira e o envio de email/push; definir geocodificação e ETA; testar os aplicativos em aparelhos Android e iOS, incluindo suspensão de tela, rede móvel e permissões; configurar assinatura e provisionamento das lojas.

A hospedagem também precisa de HTTPS, domínios/AllowedHosts/CORS explícitos, segredos externos e rotação, chaves Data Protection persistentes/protegidas, migrações e backups testados, privilégios mínimos do PostgreSQL, monitoramento e política de retenção. Revise dependências, autorização, vazamento de dados, abuso, recuperação de conta e conformidade LGPD com os responsáveis pelo produto. Rastreamento em segundo plano e push requerem implementação específica para Android/iOS e credenciais FCM/APNs. Múltiplas instâncias precisam de backplane SignalR e coordenação do processamento da outbox.

## Ambiente em nuvem desta tarefa

Foi utilizado o checkout existente em `/workspace/mibile_entrega`, sem worktree adicional. SDKs, cache de pacotes, JDK e Android SDK locais ficam fora do repositório. Não foram publicados ambiente, aplicativo, imagens ou código no GitHub.

Na instalação de workloads, o CLI .NET pula verificação de assinaturas no Linux por padrão. Os pacotes usados foram verificados separadamente com `dotnet nuget verify --all` antes da instalação offline em SDK limpo; o comando aceitou as assinaturas, mas reportou indisponibilidade de consulta de revogação de certificados na rede desta máquina. TLS não foi desabilitado. O SDK .NET e ferramentas Android foram conferidos com os checksums dos metadados oficiais. O JDK deve ser instalado como pacote verificado do fornecedor.

O build das imagens Microsoft pode depender do domínio regional de distribuição `centralus.data.mcr.microsoft.com`, além de `mcr.microsoft.com`; o primeiro foi bloqueado pela política de saída desta máquina. A execução direta com .NET e PostgreSQL não depende dessas imagens. Não foi alterada a configuração de rede publicada para contornar o bloqueio. Se desejar validar essas imagens nesta nuvem, esse destino precisa ser autorizado nas configurações do ambiente; depois repita o build com a CA da sessão via secret do BuildKit, sem gravá-la na imagem:

```bash
docker build --secret id=proxy_ca,src="$CODEX_PROXY_CERT" -f src/Brasa.Api/Dockerfile -t brasa-api:dev .
docker build --secret id=proxy_ca,src="$CODEX_PROXY_CERT" -f src/Brasa.Web/Dockerfile -t brasa-web:dev .
```

Esse bloqueio de rede não é um resultado de compilação dos Dockerfiles. Consulte as evidências em `VALIDACAO.md` para distinguir verificações executadas e pendências.
