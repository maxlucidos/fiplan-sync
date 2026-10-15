; Instalador do FIPLAN Sync (Inno Setup 6.3+). Gerado pelo build.ps1.
; As telas coletam as opções; o trabalho (testar acessos, simular, gravar a configuração
; criptografada) é feito pelo próprio FIPLAN Sync.exe, chamado com os comandos do Program.cs.

#define AppVersion "2.1.0"

; Teste: ISCC /DTeste /DPasta=<pasta> instalador\setup.iss gera uma variante que não encosta na instalação real
#ifdef Teste
  #define AppName "FIPLAN Sync TESTE"
  #define AppGuid "{{3B0A4C7D-91E2-4F55-8A6B-2C4D6E8F0A12}"
#else
  #define AppName "FIPLAN Sync"
  #define AppGuid "{{8D6F2B1E-4C3A-4F7B-9E2D-1A5C7B9E3F40}"
#endif
#ifndef Pasta
  #define Pasta "{localappdata}\fiplan-sync"
#endif

[Setup]
AppId={#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=DETRAN-MT
DefaultDirName={#Pasta}
DisableWelcomePage=no
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=FIPLAN-Sync-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\dist\fiplan-sync.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\FIPLAN Sync.exe

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
WelcomeLabel2=Este assistente vai instalar o [name] neste computador.%n%nO programa fica perto do relógio do Windows, busca as notas de empenho do DETRAN no FIPLAN e leva para o Supabase do sistema de processos, avisando quando algo mudar ou der errado.%n%nTenha em mãos o usuário e a senha do FIPLAN e a URL e a chave secret do Supabase.
FinishedLabel=O [name] foi instalado.%n%nEle fica perto do relógio do Windows (talvez dentro da setinha ^). Clique com o botão direito no ícone para atualizar na hora, ver o histórico ou mudar as configurações.

[Files]
Source: "..\dist\FIPLAN Sync.exe"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; sobras das versões anteriores
Type: files; Name: "{app}\desinstalar.bat"
Type: files; Name: "{app}\atualizar-agora.bat"
Type: files; Name: "{app}\.env"
Type: files; Name: "{app}\fiplan-sync.exe"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\FIPLAN Sync.exe"
Name: "{group}\Configurações do {#AppName}"; Filename: "{app}\FIPLAN Sync.exe"; Parameters: "--configuracoes"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; abre junto com o Windows (o usuário pode desligar nas Configurações)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\FIPLAN Sync.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\FIPLAN Sync.exe"; Parameters: "--primeira-execucao"; Description: "Abrir o FIPLAN Sync e fazer a primeira atualização"; Flags: postinstall nowait skipifsilent

[UninstallRun]
Filename: "{app}\FIPLAN Sync.exe"; Parameters: "--sair"; Flags: runhidden waituntilterminated; RunOnceId: "Fechar"
Filename: "{app}\FIPLAN Sync.exe"; Parameters: "--desagendar"; Flags: runhidden waituntilterminated; RunOnceId: "Desagendar"

[UninstallDelete]
Type: files; Name: "{app}\config.json"
Type: files; Name: "{app}\historico.jsonl"
Type: files; Name: "{app}\sync.log"
Type: files; Name: "{app}\sync.log.1"
Type: files; Name: "{app}\comparacao.csv"
Type: files; Name: "{app}\supabase.sql"
Type: dirifempty; Name: "{app}"

[Code]
var
  OraclePage: TInputQueryWizardPage;
  SupabasePage: TInputQueryWizardPage;
  ValidacaoPage: TInputOptionWizardPage;
  AtualizacaoPage: TInputOptionWizardPage;
  SimulacaoPage: TOutputMsgMemoWizardPage;
  ConfirmaCheck: TNewCheckBox;
  AgendaPage: TWizardPage;
  IntervaloCombo, InicioCombo, FimCombo: TNewComboBox;
  DasLabel, AsLabel, ResumoLabel: TNewStaticText;
  DiaChecks: array[0..6] of TNewCheckBox;
  EsperaPage: TOutputProgressWizardPage;
  MotorExtraido: Boolean;
  ArquivoAnterior: String;   { configuração da instalação anterior, em KEY=VALUE }
  ConexaoAnterior, NotificarAnterior: String;

const
  { Minutos entre atualizações; 0 = uma vez por dia; -1 = só manual (mesmos valores do Agenda.cs) }
  INTERVALOS = '15,30,60,120,180,240,0,-1';
  NOMES_DIAS = 'Seg,Ter,Qua,Qui,Sex,Sáb,Dom';

{ --- Utilitários ------------------------------------------------------------- }

function SimNao(Valor: Boolean): String;
begin
  if Valor then Result := 'sim' else Result := 'nao';
end;

function SimNaoTexto(Valor: Boolean): String;
begin
  if Valor then Result := 'Sim' else Result := 'Não';
end;

{ --- Agendamento ---------------------------------------------------------------- }

function Intervalo: Integer;
var
  Lista: TArrayOfString;
begin
  Lista := StringSplit(INTERVALOS, [','], stAll);
  Result := StrToInt(Lista[IntervaloCombo.ItemIndex]);
end;

function IndiceIntervalo(Minutos: Integer): Integer;
var
  Lista: TArrayOfString;
  I: Integer;
begin
  Result := 2;  { de hora em hora }
  Lista := StringSplit(INTERVALOS, [','], stAll);
  for I := 0 to GetArrayLength(Lista) - 1 do
    if StrToInt(Lista[I]) = Minutos then Result := I;
end;

{ Horários de meia em meia hora: o índice do combo é minutos div 30 }
function HoraTexto(Minutos: Integer): String;
begin
  Result := Format('%.2d:%.2d', [Minutos div 60, Minutos mod 60]);
end;

function MinutosDe(Texto: String; Padrao: Integer): Integer;
begin
  Result := Padrao;
  if (Length(Texto) = 5) and (Texto[3] = ':') then
    Result := StrToIntDef(Copy(Texto, 1, 2), 0) * 60 + StrToIntDef(Copy(Texto, 4, 2), 0);
end;

function DiasMarcados: String;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to 6 do
    if DiaChecks[I].Checked then Result := Result + IntToStr(I + 1);
end;

function ProblemaAgenda: String;
begin
  Result := '';
  if Intervalo < 0 then Exit;
  if DiasMarcados = '' then Result := 'Marque pelo menos um dia da semana.'
  else if (Intervalo > 0) and (FimCombo.ItemIndex <= InicioCombo.ItemIndex) then
    Result := 'O horário final precisa ser depois do inicial.';
end;

function DescreverAgenda: String;
var
  Nomes: TArrayOfString;
  Texto: String;
  I: Integer;
begin
  if Intervalo < 0 then
  begin
    Result := 'Só manual, pelo "Atualizar agora"';
    Exit;
  end;
  if Intervalo = 0 then Result := IntervaloCombo.Text + ', às ' + InicioCombo.Text
  else Result := IntervaloCombo.Text + ', das ' + InicioCombo.Text + ' às ' + FimCombo.Text;

  if DiasMarcados = '1234567' then Texto := 'todos os dias'
  else if DiasMarcados = '12345' then Texto := 'de segunda a sexta'
  else
  begin
    Nomes := StringSplit(NOMES_DIAS, [','], stAll);
    Texto := '';
    for I := 0 to 6 do
      if DiaChecks[I].Checked then
      begin
        if Texto <> '' then Texto := Texto + ', ';
        Texto := Texto + Nomes[I];
      end;
  end;
  Result := Result + ', ' + Texto;
end;

{ Liga/desliga os campos conforme a frequência e mostra o resumo do que foi escolhido }
procedure AtualizarAgenda(Sender: TObject);
var
  I: Integer;
begin
  if Intervalo = 0 then DasLabel.Caption := 'às' else DasLabel.Caption := 'das';
  AsLabel.Visible := Intervalo <> 0;
  FimCombo.Visible := Intervalo <> 0;
  InicioCombo.Enabled := Intervalo >= 0;
  FimCombo.Enabled := Intervalo >= 0;
  for I := 0 to 6 do DiaChecks[I].Enabled := Intervalo >= 0;
  if ProblemaAgenda <> '' then
  begin
    ResumoLabel.Font.Color := $1823B4;  { vermelho (BGR) }
    ResumoLabel.Caption := ProblemaAgenda;
  end
  else
  begin
    ResumoLabel.Font.Color := clWindowText;
    ResumoLabel.Caption := DescreverAgenda + '.';
  end;
end;

function LerEnv(Chave: String): String;
var
  Linhas: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if (ArquivoAnterior <> '') and LoadStringsFromFile(ArquivoAnterior, Linhas) then
    for I := 0 to GetArrayLength(Linhas) - 1 do
      if Pos(Chave + '=', Linhas[I]) = 1 then
        Result := Trim(Copy(Linhas[I], Length(Chave) + 2, MaxInt));
end;

{ Senhas trafegam como <NOME>_HEX (UTF-16 em hexadecimal): no formato KEY=VALUE um # seria
  lido como comentário, e caracteres acentuados mudariam de codificação }
function Hex(Texto: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(Texto) do Result := Result + Format('%.4x', [Ord(Texto[I])]);
end;

function DeHex(Texto: String): String;
var
  I: Integer;
begin
  Result := '';
  I := 1;
  while I + 3 <= Length(Texto) do
  begin
    Result := Result + Chr(StrToInt('$' + Copy(Texto, I, 4)));
    I := I + 4;
  end;
end;

function LerSegredo(Chave: String): String;
begin
  Result := LerEnv(Chave + '_HEX');
  if Result <> '' then Result := DeHex(Result)
  else Result := LerEnv(Chave);  { .env das versões antigas }
end;

{ Configuração da instalação anterior: a versão com app de bandeja exporta (as senhas
  estão criptografadas); a versão antiga deixava um .env }
procedure CarregarAnterior;
var
  Codigo: Integer;
begin
  ArquivoAnterior := '';
  if FileExists(ExpandConstant('{#Pasta}\FIPLAN Sync.exe')) then
  begin
    ArquivoAnterior := ExpandConstant('{tmp}\anterior.env');
    if not Exec(ExpandConstant('{#Pasta}\FIPLAN Sync.exe'), '--exportar "' + ArquivoAnterior + '"', '',
                SW_HIDE, ewWaitUntilTerminated, Codigo) or (Codigo <> 0) then
      ArquivoAnterior := '';
  end;
  if (ArquivoAnterior = '') and FileExists(ExpandConstant('{#Pasta}\.env')) then
    ArquivoAnterior := ExpandConstant('{#Pasta}\.env');
end;

procedure SalvarEnv(Arquivo: String);
begin
  SaveStringToFile(Arquivo,
    'ORACLE_USER=' + Trim(OraclePage.Values[0]) + #13#10 +
    'ORACLE_PASSWORD_HEX=' + Hex(OraclePage.Values[1]) + #13#10 +
    'ORACLE_CONNECT_STRING=' + ConexaoAnterior + #13#10 +
    'SUPABASE_URL=' + Trim(SupabasePage.Values[0]) + #13#10 +
    'SUPABASE_SECRET_KEY_HEX=' + Hex(Trim(SupabasePage.Values[1])) + #13#10 +
    'TABELA_VALIDACAO=' + SimNao(ValidacaoPage.Values[0]) + #13#10 +
    'ATUALIZAR_NOTAS=' + SimNao(AtualizacaoPage.Values[0]) + #13#10 +
    'INTERVALO=' + IntToStr(Intervalo) + #13#10 +
    'INICIO=' + InicioCombo.Text + #13#10 +
    'FIM=' + FimCombo.Text + #13#10 +
    'DIAS=' + DiasMarcados + #13#10 +
    'NOTIFICAR=' + NotificarAnterior + #13#10, False);
end;

{ Roda um programa escondido e devolve o código de saída e o que ele escreveu }
function Rodar(Exe, Params: String; var Saida: String): Integer;
var
  Resultado: TExecOutput;
  Codigo, I: Integer;
begin
  Saida := '';
  try
    if not ExecAndCaptureOutput(Exe, Params, ExtractFileDir(Exe), SW_HIDE, ewWaitUntilTerminated, Codigo, Resultado) then
    begin
      Saida := 'Não foi possível executar o programa: ' + SysErrorMessage(Codigo);
      Result := -1;
      Exit;
    end;
  except
    Saida := GetExceptionMessage;
    Result := -1;
    Exit;
  end;
  for I := 0 to GetArrayLength(Resultado.StdOut) - 1 do Saida := Saida + Resultado.StdOut[I] + #13#10;
  for I := 0 to GetArrayLength(Resultado.StdErr) - 1 do Saida := Saida + Resultado.StdErr[I] + #13#10;
  Saida := Trim(Saida);
  Result := Codigo;
end;

{ Mantém a janela respondendo (e a barra andando) enquanto espera um processo }
type
  TMensagem = record
    Janela: LongWord;
    Mensagem: LongWord;
    WParam: LongWord;
    LParam: LongWord;
    Hora: LongWord;
    X, Y: Longint;
  end;

function PeekMessage(var Msg: TMensagem; Janela: LongWord; FiltroMin, FiltroMax, Remover: LongWord): Boolean;
  external 'PeekMessageW@user32.dll stdcall';
function TranslateMessage(const Msg: TMensagem): Boolean;
  external 'TranslateMessage@user32.dll stdcall';
function DispatchMessage(const Msg: TMensagem): Longint;
  external 'DispatchMessageW@user32.dll stdcall';

procedure ProcessarMensagens;
var
  Msg: TMensagem;
begin
  while PeekMessage(Msg, 0, 0, 0, 1) do  { 1 = PM_REMOVE }
  begin
    TranslateMessage(Msg);
    DispatchMessage(Msg);
  end;
end;

{ Posiciona a barra (0 a 1000) sem o atraso da animação do Windows: a barra animada fica
  para trás do valor real, mas recuos são desenhados na hora. Por isso vai um passo além
  e volta (o máximo é 1001 para o passo além caber também nos 100%). }
procedure Barra(Posicao: Integer);
begin
  EsperaPage.ProgressBar.Max := 1001;
  EsperaPage.ProgressBar.Position := Posicao + 1;
  EsperaPage.ProgressBar.Position := Posicao;
end;

{ Antes da instalação o programa roda a partir da pasta temporária, lendo a configuração
  de um arquivo provisório (apagado junto com a pasta temporária no fim do instalador).
  Ele grava o resultado em outro arquivo; enquanto esse arquivo não aparece, a barra avança. }
function RodarAntes(Params, Mensagem: String; var Saida: String): Integer;
var
  Arquivo: String;
  Linhas: TArrayOfString;
  Codigo, Posicao, Passo, Espera, I: Integer;
begin
  Saida := '';
  Result := -1;
  EsperaPage.SetText(Mensagem, '');
  EsperaPage.SetProgress(0, 1000);
  EsperaPage.Show;
  try
    if not MotorExtraido then
    begin
      ExtractTemporaryFile('FIPLAN Sync.exe');
      MotorExtraido := True;
    end;
    SalvarEnv(ExpandConstant('{tmp}\.env'));
    Arquivo := ExpandConstant('{tmp}\resultado.txt');
    DeleteFile(Arquivo);
    if not Exec(ExpandConstant('{tmp}\FIPLAN Sync.exe'),
                Params + ' --env "' + ExpandConstant('{tmp}\.env') + '" --saida "' + Arquivo + '"',
                '', SW_HIDE, ewNoWait, Codigo) then
    begin
      Saida := 'Não foi possível executar o programa: ' + SysErrorMessage(Codigo);
      Exit;
    end;

    { Sem como saber o progresso real, a barra anda rápido no começo e desacelera,
      sem chegar ao fim antes de o comando terminar. Fica pelo menos 0,8 s na tela,
      para não piscar quando o comando é rápido. }
    Posicao := 0;
    Espera := 0;
    while not FileExists(Arquivo) or (Espera < 8) do
    begin
      Passo := (950 - Posicao) div 25;
      if Passo < 1 then Passo := 0;
      Posicao := Posicao + Passo;
      Barra(Posicao);
      ProcessarMensagens;
      Sleep(100);
      Espera := Espera + 1;
      if Espera > 6000 then  { 10 minutos }
      begin
        Saida := 'O programa demorou demais para responder.';
        Exit;
      end;
    end;
    { Terminou: completa a barra aos poucos e mostra os 100% por 0,7 s antes de seguir }
    while Posicao < 1000 do
    begin
      Posicao := Posicao + 40;
      if Posicao > 1000 then Posicao := 1000;
      Barra(Posicao);
      ProcessarMensagens;
      Sleep(15);
    end;
    for I := 1 to 7 do
    begin
      ProcessarMensagens;
      Sleep(100);
    end;

    if not LoadStringsFromFile(Arquivo, Linhas) or (GetArrayLength(Linhas) = 0) then
    begin
      Saida := 'Não consegui ler o resultado do programa.';
      Exit;
    end;
    Result := StrToIntDef(Copy(Linhas[0], 8, 10), 1);  { "CODIGO=n" }
    for I := 1 to GetArrayLength(Linhas) - 1 do Saida := Saida + Linhas[I] + #13#10;
    Saida := Trim(Saida);
  finally
    EsperaPage.Hide;
  end;
end;

function ConferirTabela: Boolean;
var
  Codigo: Integer;
  Saida: String;
begin
  Result := False;
  while True do
  begin
    Codigo := RodarAntes('--tabela-existe', 'Verificando a tabela no Supabase...', Saida);
    if Codigo = 0 then
    begin
      Result := True;
      Exit;
    end;
    if Codigo <> 2 then
    begin
      MsgBox(Saida, mbError, MB_OK);
      Exit;
    end;
    if MsgBox('A tabela fiplan_notas_empenho ainda não existe no Supabase.' + #13#10#13#10 +
              'Ao clicar em OK, o SQL Editor do Supabase abre no navegador com o SQL já copiado. ' +
              'Cole com Ctrl+V, clique em Run e volte aqui.', mbInformation, MB_OKCANCEL) <> IDOK then Exit;
    RodarAntes('--abrir-sql', 'Abrindo o SQL Editor...', Saida);
    if MsgBox('Depois de rodar o SQL no Supabase, clique em OK para verificar.',
              mbInformation, MB_OKCANCEL) <> IDOK then Exit;
  end;
end;

{ --- Telas ------------------------------------------------------------------- }

function Rotulo(Pagina: TWizardPage; Texto: String; X, Y: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(Pagina);
  Result.Parent := Pagina.Surface;
  Result.Caption := Texto;
  Result.Left := X;
  Result.Top := Y;
end;

function Lista(Pagina: TWizardPage; X, Y, Largura: Integer): TNewComboBox;
begin
  Result := TNewComboBox.Create(Pagina);
  Result.Parent := Pagina.Surface;
  Result.Style := csDropDownList;
  Result.Left := X;
  Result.Top := Y;
  Result.Width := Largura;
  Result.OnChange := @AtualizarAgenda;
end;

{ Frequência, horário e dias da semana, preenchidos com a configuração anterior }
procedure CriarPaginaAgenda;
var
  Nomes: TArrayOfString;
  Coluna, Linha, I, IntervaloAnterior, InicioAnterior, FimAnterior: Integer;
  Dias: String;
  Nota: TNewStaticText;
begin
  { Configuração anterior (instaladores até a 2.1 tinham só 4 opções fixas) }
  InicioAnterior := MinutosDe(LerEnv('INICIO'), 7 * 60);
  FimAnterior := MinutosDe(LerEnv('FIM'), 19 * 60);
  Dias := LerEnv('DIAS');
  if LerEnv('INTERVALO') <> '' then IntervaloAnterior := StrToIntDef(LerEnv('INTERVALO'), 60)
  else if LerEnv('FREQUENCIA') = 'meia' then IntervaloAnterior := 30
  else if LerEnv('FREQUENCIA') = 'manual' then IntervaloAnterior := -1
  else if LerEnv('FREQUENCIA') = 'dia' then
  begin
    IntervaloAnterior := 0;
    InicioAnterior := 8 * 60;
  end
  else IntervaloAnterior := 60;
  if Dias = '' then Dias := '12345';

  AgendaPage := CreateCustomPage(SimulacaoPage.ID,
    'Frequência', 'De quanto em quanto tempo buscar os dados no FIPLAN.');
  Coluna := ScaleX(80);

  Rotulo(AgendaPage, 'Frequência:', 0, ScaleY(4));
  IntervaloCombo := Lista(AgendaPage, Coluna, 0, ScaleX(260));
  IntervaloCombo.Items.Add('A cada 15 minutos');
  IntervaloCombo.Items.Add('A cada 30 minutos');
  IntervaloCombo.Items.Add('De hora em hora');
  IntervaloCombo.Items.Add('A cada 2 horas');
  IntervaloCombo.Items.Add('A cada 3 horas');
  IntervaloCombo.Items.Add('A cada 4 horas');
  IntervaloCombo.Items.Add('Uma vez por dia');
  IntervaloCombo.Items.Add('Só manual (pelo "Atualizar agora")');
  IntervaloCombo.ItemIndex := IndiceIntervalo(IntervaloAnterior);

  Linha := ScaleY(40);
  Rotulo(AgendaPage, 'Horário:', 0, Linha + ScaleY(4));
  DasLabel := Rotulo(AgendaPage, 'das', Coluna, Linha + ScaleY(4));
  InicioCombo := Lista(AgendaPage, Coluna + ScaleX(28), Linha, ScaleX(70));
  AsLabel := Rotulo(AgendaPage, 'às', Coluna + ScaleX(108), Linha + ScaleY(4));
  FimCombo := Lista(AgendaPage, Coluna + ScaleX(130), Linha, ScaleX(70));
  for I := 0 to 47 do
  begin
    InicioCombo.Items.Add(HoraTexto(I * 30));
    FimCombo.Items.Add(HoraTexto(I * 30));
  end;
  InicioCombo.ItemIndex := InicioAnterior div 30;
  FimCombo.ItemIndex := FimAnterior div 30;

  Linha := ScaleY(80);
  Rotulo(AgendaPage, 'Dias:', 0, Linha + ScaleY(2));
  Nomes := StringSplit(NOMES_DIAS, [','], stAll);
  for I := 0 to 6 do
  begin
    DiaChecks[I] := TNewCheckBox.Create(AgendaPage);
    DiaChecks[I].Parent := AgendaPage.Surface;
    DiaChecks[I].Caption := Nomes[I];
    DiaChecks[I].Left := Coluna + I * ScaleX(48);
    DiaChecks[I].Top := Linha;
    DiaChecks[I].Width := ScaleX(46);
    DiaChecks[I].Checked := Pos(IntToStr(I + 1), Dias) > 0;
    DiaChecks[I].OnClick := @AtualizarAgenda;
  end;

  ResumoLabel := Rotulo(AgendaPage, '', Coluna, ScaleY(116));
  ResumoLabel.AutoSize := False;
  ResumoLabel.WordWrap := True;
  ResumoLabel.Width := AgendaPage.SurfaceWidth - Coluna;
  ResumoLabel.Height := ScaleY(34);

  Nota := Rotulo(AgendaPage, 'O FIPLAN Sync avisa por notificação quando alguma nota mudar ou quando der erro. ' +
    'Se o computador estiver desligado no horário, ele atualiza assim que ligar. ' +
    'Tudo isso pode ser mudado depois, nas Configurações.', 0, ScaleY(164));
  Nota.AutoSize := False;
  Nota.WordWrap := True;
  Nota.Width := AgendaPage.SurfaceWidth;
  Nota.Height := ScaleY(48);

  AtualizarAgenda(nil);
end;

procedure InitializeWizard;
begin
  CarregarAnterior;
  ConexaoAnterior := LerEnv('ORACLE_CONNECT_STRING');
  NotificarAnterior := LerEnv('NOTIFICAR');
  if NotificarAnterior = '' then NotificarAnterior := 'mudancas';

  OraclePage := CreateInputQueryPage(wpWelcome,
    'Conexão com o FIPLAN', 'Acesso à view de notas de empenho.',
    'Informe o usuário e a senha de acesso ao FIPLAN. A senha fica guardada criptografada, só para o seu usuário do Windows.' + #13#10 +
    'Este computador precisa estar na rede do DETRAN.');
  OraclePage.Add('Usuário:', False);
  OraclePage.Add('Senha:', True);
  OraclePage.Values[0] := LerEnv('ORACLE_USER');
  if OraclePage.Values[0] = '' then OraclePage.Values[0] := 'DETRAN_FIPLAN_USER';
  OraclePage.Values[1] := LerSegredo('ORACLE_PASSWORD');

  SupabasePage := CreateInputQueryPage(OraclePage.ID,
    'Conexão com o Supabase', 'Onde o sistema guarda os dados.',
    'Informe a URL do projeto e a chave secret. A chave fica guardada criptografada, só para o seu usuário do Windows.' + #13#10 +
    'No Supabase: Project Settings > API Keys > Secret keys.');
  SupabasePage.Add('URL do projeto (https://xxxx.supabase.co):', False);
  SupabasePage.Add('Chave secret (sb_secret_...):', True);
  SupabasePage.Values[0] := LerEnv('SUPABASE_URL');
  SupabasePage.Values[1] := LerSegredo('SUPABASE_SECRET_KEY');

  ValidacaoPage := CreateInputOptionPage(SupabasePage.ID,
    'Tabela de validação', 'Uma cópia do FIPLAN para conferir os números.',
    'A tabela fiplan_notas_empenho guarda as notas do FIPLAN, e a view vw_fiplan_comparacao mostra ' +
    'cada nota do sistema lado a lado com o FIPLAN (ok, diferente ou não encontrada).' + #13#10#13#10 +
    'Não altera nada do sistema.', False, False);
  ValidacaoPage.Add('Usar a tabela de validação (recomendado)');
  ValidacaoPage.Values[0] := LerEnv('TABELA_VALIDACAO') <> 'nao';

  AtualizacaoPage := CreateInputOptionPage(ValidacaoPage.ID,
    'Atualização das notas', 'Manter pagamentos e estornos iguais aos do FIPLAN.',
    'Atualiza pagamentos e estornos das notas em processo_notas_empenho com os valores do FIPLAN. ' +
    'O valor empenhado nunca é alterado.' + #13#10#13#10 +
    'Ao avançar, o instalador mostra a lista do que mudaria, antes de aplicar.', False, False);
  AtualizacaoPage.Add('Atualizar as notas do sistema automaticamente');
  AtualizacaoPage.Values[0] := LerEnv('ATUALIZAR_NOTAS') = 'sim';

  SimulacaoPage := CreateOutputMsgMemoPage(AtualizacaoPage.ID,
    'Conferência', 'O que mudaria nas notas do sistema.',
    'As mudanças são aplicadas na primeira atualização e, depois, a cada atualização agendada.', '');
  SimulacaoPage.RichEditViewer.Font.Name := 'Consolas';
  SimulacaoPage.RichEditViewer.WordWrap := False;
  SimulacaoPage.RichEditViewer.ScrollBars := ssBoth;
  ConfirmaCheck := TNewCheckBox.Create(SimulacaoPage);
  ConfirmaCheck.Parent := SimulacaoPage.Surface;
  ConfirmaCheck.Caption := '&Conferi e quero aplicar essas mudanças';
  ConfirmaCheck.Left := 0;
  ConfirmaCheck.Width := SimulacaoPage.SurfaceWidth;
  ConfirmaCheck.Height := ScaleY(17);
  ConfirmaCheck.Top := SimulacaoPage.SurfaceHeight - ConfirmaCheck.Height;
  SimulacaoPage.RichEditViewer.Height := ConfirmaCheck.Top - SimulacaoPage.RichEditViewer.Top - ScaleY(8);

  CriarPaginaAgenda;

  EsperaPage := CreateOutputProgressPage('Aguarde', '');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = SimulacaoPage.ID) and not AtualizacaoPage.Values[0];
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Codigo: Integer;
  Saida: String;
begin
  Result := True;

  if CurPageID = OraclePage.ID then
  begin
    if (Trim(OraclePage.Values[0]) = '') or (OraclePage.Values[1] = '') then
    begin
      MsgBox('Preencha o usuário e a senha do FIPLAN.', mbError, MB_OK);
      Result := False;
    end
    else if RodarAntes('--testar-oracle', 'Testando o acesso ao FIPLAN...', Saida) <> 0 then
    begin
      MsgBox(Saida, mbError, MB_OK);
      Result := False;
    end;
  end

  else if CurPageID = SupabasePage.ID then
  begin
    if (Trim(SupabasePage.Values[0]) = '') or (Trim(SupabasePage.Values[1]) = '') then
    begin
      MsgBox('Preencha a URL do projeto e a chave secret.', mbError, MB_OK);
      Result := False;
    end
    else
    begin
      Codigo := RodarAntes('--testar-supabase', 'Testando a conexão com o Supabase...', Saida);
      if Codigo <> 0 then
      begin
        MsgBox(Saida, mbError, MB_OK);
        Result := False;
      end
      else
        SupabasePage.Values[0] := Trim(Copy(Saida, 4, MaxInt));  { "OK <url normalizada>" }
    end;
  end

  else if CurPageID = ValidacaoPage.ID then
  begin
    if ValidacaoPage.Values[0] then Result := ConferirTabela;
  end

  else if CurPageID = AtualizacaoPage.ID then
  begin
    if not ValidacaoPage.Values[0] and not AtualizacaoPage.Values[0] then
    begin
      MsgBox('Ligue pelo menos uma opção: a tabela de validação (etapa anterior) ou a atualização das notas.',
             mbError, MB_OK);
      Result := False;
    end
    else if AtualizacaoPage.Values[0] then
    begin
      Codigo := RodarAntes('--simular', 'Comparando o sistema com o FIPLAN (uns 10 segundos)...', Saida);
      if Codigo <> 0 then
      begin
        MsgBox(Saida, mbError, MB_OK);
        Result := False;
      end
      else
      begin
        SimulacaoPage.RichEditViewer.Lines.Text := Saida;
        ConfirmaCheck.Checked := False;
      end;
    end;
  end

  else if CurPageID = SimulacaoPage.ID then
  begin
    if not ConfirmaCheck.Checked then
    begin
      MsgBox('Marque "Conferi e quero aplicar essas mudanças" para continuar, ou volte e desligue a atualização.',
             mbInformation, MB_OK);
      Result := False;
    end;
  end

  else if CurPageID = AgendaPage.ID then
  begin
    if ProblemaAgenda <> '' then
    begin
      MsgBox(ProblemaAgenda, mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result :=
    'FIPLAN:' + NewLine + Space + 'usuário ' + OraclePage.Values[0] + NewLine + NewLine +
    'Supabase:' + NewLine + Space + SupabasePage.Values[0] + NewLine + NewLine +
    'Tabela de validação:' + NewLine + Space + SimNaoTexto(ValidacaoPage.Values[0]) + NewLine + NewLine +
    'Atualizar as notas do sistema:' + NewLine + Space + SimNaoTexto(AtualizacaoPage.Values[0]) + NewLine + NewLine +
    'Frequência:' + NewLine + Space + DescreverAgenda + NewLine + NewLine +
    'Pasta:' + NewLine + Space + ExpandConstant('{app}');
end;

{ --- Instalação --------------------------------------------------------------- }

{ Fecha o app de bandeja da instalação anterior para poder substituir os arquivos }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Codigo: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\FIPLAN Sync.exe')) then
    Exec(ExpandConstant('{app}\FIPLAN Sync.exe'), '--sair', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Pasta, Arquivo, Saida: String;
  Codigo: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  Pasta := ExpandConstant('{app}');

  { Configuração: o app de bandeja grava com as senhas criptografadas }
  WizardForm.StatusLabel.Caption := 'Gravando a configuração...';
  Arquivo := ExpandConstant('{tmp}\config.env');
  SalvarEnv(Arquivo);
  Codigo := Rodar(Pasta + '\FIPLAN Sync.exe', '--importar "' + Arquivo + '"', Saida);
  DeleteFile(Arquivo);
  if Codigo <> 0 then
    MsgBox('Não consegui gravar a configuração:' + #13#10#13#10 + Saida + #13#10#13#10 +
           'Abra o FIPLAN Sync e preencha em Configurações.', mbError, MB_OK);

  { Versões anteriores agendavam pelo Agendador de Tarefas; agora quem agenda é o app }
  Rodar(Pasta + '\FIPLAN Sync.exe', '--desagendar', Saida);
end;

function InitializeUninstall: Boolean;
var
  Codigo: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\FIPLAN Sync.exe')) then
    Exec(ExpandConstant('{app}\FIPLAN Sync.exe'), '--sair', '', SW_HIDE, ewWaitUntilTerminated, Codigo);
end;
