# FIPLAN Sync

Leva as notas de empenho do DETRAN (UO 19301) da view `ACWVW0111` do FIPLAN para o Supabase do sistema de processos. Fica perto do relógio do Windows, atualiza no horário agendado e avisa quando alguma nota muda ou quando dá erro.

É entregue como **`FIPLAN-Sync-Setup.exe`** (~4 MB). Quem instala não precisa de administrador nem de mais nada instalado: o programa usa o .NET Framework que já vem no Windows, e o driver do Oracle vai embutido.

## O que ele faz

| Opção | O que faz |
|---|---|
| **Enviar diferenças para validação** (recomendado) | As notas de `processo_notas_empenho` com pago ou estorno diferente do FIPLAN viram linhas `pendente` em `staging_notas_empenho`, para um operador validar pelo sistema. Não altera as notas. |
| **Atualizar notas** | Atualiza `pagamentos` e `estornos` em `processo_notas_empenho` direto com os valores do FIPLAN, sem validação. O `valor` empenhado não é alterado; divergências de valor só aparecem no histórico. |

### Como as diferenças vão para a staging

Só entram as notas cadastradas no sistema cujo pago ou estorno difere do FIPLAN (mais de meio centavo). Cada uma vira **uma** linha `pendente`:

| Coluna da staging | Valor |
|---|---|
| `empenho_id` | `processo_notas_empenho.id` |
| `processo_id`, `numero_empenho` | da nota no sistema |
| `valor_pgto_ia` / `valor_estorno_ia` | pago e estornado no FIPLAN |
| `contexto_extracao` | `FIPLAN Sync: view ACWVW0111 em <data> · empenhado · pago · estornado · saldo (no sistema: pago · estornos)` |
| `status` | `pendente` |

A cada atualização o programa mantém essas pendências em dia: cria as que faltam, atualiza os valores se o FIPLAN mudou e **apaga** as que não são mais necessárias (a nota foi corrigida). Ele só mexe em linhas `pendente` cujo `contexto_extracao` começa com `FIPLAN Sync:`; linhas de outras origens (ex.: extraídas por IA) e as já validadas nunca são alteradas. Notas sem `processo_id` não vão para a staging (a coluna é obrigatória) e aparecem como aviso no histórico.

O sistema calcula o saldo no front (`valor − pagamentos − estornos`). Como o pago é calculado como `empenhado − estornado − saldo do FIPLAN`, o saldo exibido fica igual ao do FIPLAN.

## Instalação

Precisa estar na rede do DETRAN (192.168.221.21:1521). Abra o `FIPLAN-Sync-Setup.exe`:

1. **Conexão com o FIPLAN**: usuário e senha da view. O instalador testa antes de avançar.
2. **Conexão com o Supabase**: URL do projeto e **chave secret** (Project Settings > API Keys > Secret keys).
3. **Validação das diferenças**: o instalador confere se a `staging_notas_empenho` existe com as colunas usadas. Não é preciso rodar SQL.
4. **Atualização das notas**: ao ligar, a etapa seguinte lista o que mudaria e só avança com "Conferi e quero aplicar" marcado.
5. **Frequência**: de 15 em 15 minutos até a cada 4 horas, uma vez por dia ou só manual; o horário (das __ às __) e os dias da semana.

Senha e chave ficam guardadas criptografadas (DPAPI do Windows): só o mesmo usuário, no mesmo computador, consegue ler.

Rodar o instalador de novo atualiza o programa e traz as telas preenchidas com a configuração atual.

## Uso

Clique com o botão direito no ícone "F" perto do relógio (talvez dentro da setinha ^):

- **Atualizar agora**
- **Histórico**: uma linha por atualização; ao selecionar, mostra quais notas mudaram (antes → depois) ou o erro.
- **Configurações**: acesso ao FIPLAN e ao Supabase (com botões de teste), opções, notificações, "abrir com o Windows" e o **Agendamento** (frequência, horário e dias da semana).
- **Pausar atualizações** e **Sair**.
- **Procurar atualização…**: verifica se há versão nova do programa (a versão instalada aparece logo abaixo).

### Atualização do programa

Dois minutos depois de abrir e depois uma vez por dia, o programa consulta a release mais nova em [github.com/maxlucidos/fiplan-sync](https://github.com/maxlucidos/fiplan-sync/releases). Se houver versão maior que a instalada, mostra a notificação "FIPLAN Sync X disponível"; ao confirmar, baixa o instalador, confere o SHA-256 publicado pelo GitHub e roda em modo de atualização (`/ATUALIZACAO`): sem telas, sem refazer os testes de acesso, mantendo a configuração, e reabre sozinho. Sem internet, ele tenta de novo no dia seguinte, sem avisar.

O repositório consultado pode ser trocado no `config.json` (`"repositorioAtualizacao": "dono/repositório"`).

| Cor do ícone | Significado |
|---|---|
| Azul | Tudo certo |
| Laranja | Atualizando |
| Vermelho | A última atualização deu erro (passe o mouse ou abra o Histórico) |
| Cinza | Pausado ou falta configurar |

Se o computador estiver desligado no horário agendado, ele atualiza um minuto depois de ligar. Um mesmo erro só é avisado uma vez, para não repetir a notificação a cada hora. Se as notificações do Windows estiverem desligadas, o ícone continua mostrando o estado e as Configurações avisam.

Arquivos em `%LOCALAPPDATA%\fiplan-sync`: `config.json` (configuração, segredos criptografados), `historico.jsonl` (histórico) e `sync.log` (registro completo, para suporte).

## Desenvolvimento

```
src\          código C# (app de bandeja, comandos e sincronização)
lib\          driver do Oracle para .NET e sua licença
instalador\   script do Inno Setup
build.ps1     gera o programa e o instalador em dist\
```

```
powershell -ExecutionPolicy Bypass -File build.ps1    # gera dist\FIPLAN Sync.exe e dist\FIPLAN-Sync-Setup.exe
```

Precisa do Inno Setup 6.3+ (`winget install JRSoftware.InnoSetup`). O C# é compilado com o `csc.exe` do .NET Framework 4.8 que já vem no Windows (C# 5).

| Arquivo | Conteúdo |
|---|---|
| `src\Program.cs` | Ponto de entrada: app de bandeja ou comandos de linha de comando |
| `src\AppBandeja.cs` | Ícone, menu, agendamento e notificações |
| `src\Fiplan.cs` | Leitura da view no Oracle |
| `src\Supabase.cs` | API REST do Supabase |
| `src\Sincronizacao.cs` | Sincronização, comparação e simulação |
| `src\Comandos.cs` | Comandos usados pelo instalador e pelas Configurações |
| `src\Config.cs`, `Agenda.cs`, `Historico.cs`, `Icones.cs`, `Janela*.cs` | Configuração, horários, histórico, ícones e janelas |
| `lib\Oracle.ManagedDataAccess.dll` | Driver oficial da Oracle para .NET (19.18.0, sem dependências), embutido no .exe |
| `instalador\setup.iss` | Instalador (Inno Setup) |

Para testar comandos sem instalar, use um arquivo de configuração (modelo em `.env.example`):

```
"dist\FIPLAN Sync.exe" --testar-oracle --env .env
"dist\FIPLAN Sync.exe" --simular --env .env
"dist\FIPLAN Sync.exe" --dry-run --env .env
"dist\FIPLAN Sync.exe" --comparar --env .env     # gera comparacao.csv
```

### Publicar uma versão

1. Suba o `AppVersion` em `instalador\setup.iss` (é a única fonte da versão: o `build.ps1` também a grava no `.exe`).
2. Rode o `build.ps1` e faça o commit.
3. Crie a release no GitHub com a tag `vX.Y.Z` e anexe o `dist\FIPLAN-Sync-Setup.exe` **com esse nome**: é por ele que os programas instalados encontram o instalador.

Para testar o instalador sem encostar na instalação real: `ISCC /DTeste /DPasta=<pasta> instalador\setup.iss`. Isso gera uma variante com outro nome e outro AppId; defina `FIPLAN_TAREFA` para o `--desagendar` usar outro nome de tarefa.

## Como os valores são calculados

| Campo | Cálculo |
|---|---|
| empenhado | `VALR_EMP` |
| estornado | `TOTAL_ANULADO + TOTAL_CANCELADO_RPNP` |
| saldo | `TOTAL_LIQUIDAR + TOTAL_EM_LIQ + TOTAL_LIQ_PAGAR` |
| pago | empenhado − estornado − saldo |
| fonte | 9º segmento de `CODG_DOTACAO_ORCAMENTARIA` |

A view não tem o **Total Cancelado RPP** do extrato (cancelamento de restos a pagar processados, ou seja, de valor já liquidado). Nessas notas o pago calculado sai maior e o estornado menor que o real, pelo mesmo valor. Como estorno não diminui, quando o estornado da view é menor que o do sistema a nota não é sugerida, só registrada como aviso no histórico.

O `TOTAL_PAGO` da view não é usado porque reflete só o saldo contábil do exercício e diverge do valor real na maioria das notas de anos anteriores (restos a pagar).

O sistema guarda o número como `19301.0001.15.000293-1` e o FIPLAN como `193010001150002931`. O cruzamento usa só os dígitos.
