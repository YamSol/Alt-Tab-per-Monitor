# Spec v2: Alternador de janelas por monitor (Windows)

Oct 22, 2026 · @yam

Ferramenta em segundo plano, sem admin, que limita o Alt+Tab às janelas do monitor onde está o cursor. As demais são ocultadas com SW\_HIDE e restauradas depois da seleção.

## 1. Visão geral e objetivos

Ferramenta em segundo plano, executada no contexto do usuário (sem admin). Identifica o monitor onde está o cursor e limita o Alt+Tab às janelas desse monitor. As janelas de outros monitores são ocultadas com `SW_HIDE` enquanto o seletor está aberto e restauradas ao estado original depois da seleção.

- **O1.** Identificar, no acionamento, o monitor que contém o cursor.
- **O2.** Exibir no seletor do sistema apenas as janelas do monitor atual.
- **O3.** Após seleção ou cancelamento, restaurar exatamente o estado das janelas que a ferramenta ocultou.
- **O4.** Nunca alterar o estado de uma janela que a ferramenta não ocultou.
- **O5.** Tratar cada janela distintamente, inclusive janelas secundárias (owned), sem agrupar por aplicativo.

## 2. Não objetivos e definições

**Não objetivos**

- Gerenciar layout, tamanho ou posição das janelas.
- Substituir o seletor do sistema por um seletor próprio (fora do escopo desta versão).
- Operar sobre janelas de processos elevados, salvo se a ferramenta também rodar elevada.
- Suportar Linux ou macOS.
- Funcionar em tela cheia exclusiva (DirectX exclusivo), onde o seletor do sistema não aparece.

**Definições**

- **Janela top-level:** `HWND` sem dono (`GW_OWNER` = 0), listada no Alt+Tab quando visível ou minimizada.
- **Janela owned:** `HWND` de nível superior com dono (diálogos, paletas, pop-ups, janelas de "Salvar como"). Tem identidade própria e é tratada como unidade independente.
- **Janela child:** `HWND` com `WS_CHILD`, parte do conteúdo de outra janela. Nunca é ocultada.
- **Aba:** elemento interno de uma janela, sem `HWND` próprio. Não é unidade de ocultação; pertence à janela que a contém.
- **Monitor da janela:** monitor com maior área de interseção com a janela (`MONITOR_DEFAULTTONEAREST`).
- **Monitor atual:** monitor que contém o cursor no acionamento (`MonitorFromPoint`).
- **Snapshot:** registro, por janela ocultada pela ferramenta, de `HWND`, processo, estado (`GetWindowPlacement`), visibilidade e posição na ordem Z.

## 3. Classificação e regras de ocultação

Cada tipo de janela tem uma regra. Ocultar uma top-level não oculta suas owned automaticamente: cada uma é avaliada pela própria posição de monitor.

| Tipo | Ocultada? | Regra |
| --- | --- | --- |
| Top-level visível, com título, fora da lista de exclusões | Sim, se estiver em outro monitor | Unidade principal |
| Top-level minimizada | Sim, se estiver em outro monitor | Registrar `SW_SHOWMINIMIZED` no snapshot |
| Owned com título, não modal | Sim, se estiver em outro monitor | Tratada independentemente da dona |
| Owned modal (dono com `IsWindowEnabled` = FALSE) | Não | Ocultar pode travar o app |
| Owned sem título | Não | Geralmente é auxiliar interno |
| `WS_EX_TOOLWINDOW` | Não | Não aparece no Alt+Tab padrão |
| Child | Nunca | Pertence à janela pai |
| Já oculta antes do acionamento | Não | Não entra no snapshot; não é restaurada pela ferramenta |

Se a dona for ocultada e a owned ficar exibida sozinha, isso deve ser coberto pelos testes (seção 6) antes de decidir se a owned acompanha a dona.

## 4. Requisitos funcionais

**RF1. Detecção de monitor.** `GetCursorPos` para o cursor e `MonitorFromPoint` para o monitor atual. Para cada janela candidata, `MonitorFromWindow` com `MONITOR_DEFAULTTONEAREST`. Feito no acionamento, não em polling.

**RF2. Interceptação do Alt+Tab.** Hook `WH_KEYBOARD_LL` que observa Alt e Tab (captura Alt+Tab e Alt+Shift+Tab). Ao detectar o início, dispara a ocultação antes do seletor do sistema. Não registra nem transmite texto digitado.

**RF3. Ocultação.** Antes do seletor aparecer, `ShowWindow(hwnd, SW_HIDE)` nas candidatas fora do monitor atual. Snapshot de cada janela gravado antes de ocultá-la, em `%LOCALAPPDATA%\MonitorSwitch\state.json`, antes da primeira ocultação.

**RF4. Fim do seletor e restauração.** Na confirmação (soltar Alt), restaurar todas as janelas do snapshot, na ordem Z registrada, de baixo para cima, com `SetWindowPlacement`. Dar foco à escolhida com `SetForegroundWindow`; se falhar, usar `AttachThreadInput`. No cancelamento (Esc), restaurar tudo sem alterar o foco.

**RF5. Mudanças durante o seletor.** Janela do snapshot fechada: remover a entrada, sem restaurar. Janela alterada pelo próprio app: registrar o estado atual antes de restaurar, para não sobrescrever a mudança. Janela nova: não é ocultada nem listada até o próximo acionamento.

**RF6. Invariante de estado.** Após a seleção, visibilidade, minimização, maximização e posição das janelas devem ser iguais às de antes do Alt+Tab, exceto a escolhida, que recebe foco. Verificado pelo teste da seção 6.

**RF7. Recuperação após falha.** Na inicialização, se `state.json` existir, restaurar e apagar o arquivo. Tratar encerramento normal, `Ctrl+C`, logoff e `WM_QUERYENDSESSION` restaurando antes de sair. Atalho de emergência que restaura todas as janelas do snapshot.

**RF8. Controle.** Ícone na bandeja com ativar/desativar, atalho, lista de exclusões por nome de processo e modo de operação. Modo **somente visualizar** (padrão para testes): calcula o filtro e registra o que seria ocultado, sem alterar nenhuma janela.

**RF9. Desktops virtuais.** Considerar apenas as janelas do desktop atual (`IVirtualDesktopManager::IsWindowOnCurrentVirtualDesktop`). A configuração de Alt+Tab entre desktops deve ser verificada na máquina de teste antes da implementação.

## 5. Requisitos não funcionais, decisões e riscos

**Requisitos não funcionais**

- **Latência:** seletor visível em até 150 ms após o atalho, com até 30 janelas.
- **Estabilidade:** se o hook falhar, é desinstalado e o teclado não trava.
- **Consumo:** CPU em repouso abaixo de 0,5%. Cache atualizado por eventos (`SetWinEventHook` com `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE`, `EVENT_SYSTEM_FOREGROUND`), sem polling.
- **Compatibilidade:** Windows 10 (21H2+) e Windows 11.
- **Privacidade:** não gravar conteúdo de janelas nem teclas digitadas.

**Decisões técnicas**

| Decisão | Escolha | Alternativa descartada | Motivo |
| --- | --- | --- | --- |
| Ocultação | `SW_HIDE` com snapshot | Minimizar, mover para fora da tela | Minimizar não remove do Alt+Tab; mover altera posição |
| Linguagem | C# (.NET 8) com P/Invoke | AutoHotkey, Python | Controle sobre hooks, estado e recuperação; sem exigir admin |
| Unidade | `HWND` individual | Agrupar por processo | Tratar subjanelas distintamente |
| Abas | Não tratadas | Controlar abas | Não possuem `HWND`; não há API pública |

**Riscos e casos de borda**

- **Apps que pausam ao serem ocultados** (players, editores, jogos): lista de exclusões obrigatória.
- **Modais e owned críticas:** ocultar pode travar o app. Modais são ignoradas.
- **Owned órfã:** dona oculta e owned visível. Testar e decidir se a owned acompanha a dona.
- **Janela oculta pelo próprio app:** não entra no snapshot, logo não é restaurada. Correto por construção.
- **Ordem Z:** restaurar de baixo para cima reduz a alteração, mas não garante a mesma ordem.
- **Foco bloqueado:** `SetForegroundWindow` pode falhar; fallback com `AttachThreadInput`.
- **UIPI:** janelas de processos elevados não podem ser manipuladas por processo não elevado. Ignorar e documentar.
- **Corrida do seletor:** o sistema pode exibir o seletor antes de a ocultação terminar. Medir o tempo do fluxo e testar com janelas grandes.
- **Janela presa oculta:** mitigada por RF7 e pelo atalho de emergência.

## 6. Critérios de aceitação e milestones

**Critérios de aceitação**

1. Dois monitores, cursor no monitor 1, Alt+Tab: o seletor lista só janelas do monitor 1.
2. Esc durante o seletor: todas as janelas ocultadas voltam ao estado do snapshot, incluindo minimizadas e maximizadas.
3. Após selecionar, as janelas não escolhidas mantêm o estado anterior. Teste automatizado: `EnumWindows` e `GetWindowPlacement` antes e depois, com comparação.
4. Janela já minimizada antes do Alt+Tab continua minimizada depois.
5. Janela já oculta antes do Alt+Tab continua oculta depois e não é restaurada pela ferramenta.
6. Owned com título em outro monitor é tratada independentemente da dona.
7. Owned modal não é ocultada.
8. Matar o processo com janelas ocultas e reiniciar: as janelas são restauradas.
9. Modo "somente visualizar" não altera nenhuma janela.
10. Latência abaixo de 150 ms com 30 janelas.

**Milestones**

Cada sessão do Claude Code começa lendo esta spec e entrega só o escopo listado.

- **Sessão 1, M1 (1–2 dias):** programa C# que lista, por monitor, as janelas top-level e owned do monitor atual, sem ocultar nada. Valida RF1 e a seção 3.
- **Sessão 2, M2a (2 dias):** snapshot em `state.json`, sem ocultar ainda. Valida a gravação de RF3 e a leitura de RF7 na inicialização.
- **Sessão 3, M2b (2 dias):** `SW_HIDE`, `SetWindowPlacement` e `SetForegroundWindow`, com disparo manual. Valida RF3, RF4 e RF6.
- **Sessão 4, M2c (1 dia):** testes automatizados do critério 3 e do critério 4 da seção 6. Valida RF6.
- **Sessão 5, M3a (2 dias):** hook `WH_KEYBOARD_LL` que detecta Alt+Tab, sem ocultar. Valida RF2.
- **Sessão 6, M3b (2–3 dias):** liga o hook à ocultação e à restauração automáticas. Valida RF5.
- **Sessão 7, M4a (2 dias):** recuperação de falhas completa: encerramento, logoff e atalho de emergência. Valida RF7.
- **Sessão 8, M4b (1–2 dias):** bandeja e lista de exclusões. Valida RF8 e RF9.
- **Sessão 9, M5 (2–3 dias):** critérios 6 a 10 da seção 6, riscos da seção 5, empacotamento e documentação.

## 7. Questões em aberto

1. **Owned de janela oculta: acompanhar a dona.** Resposta mais provável: se a dona for ocultada, a owned também é. Uma owned visível sem a dona fica órfã e confunde o usuário. Validar no M2.
2. **Microsoft Store (`ApplicationFrameHost`): tratar como top-level normal.** Resposta mais provável: a janela visível é a do app, dentro do frame, então ocultar o frame basta. Confirmar no M1 antes de criar regra própria.
3. **Lista de exclusões: começar vazia.** Resposta mais provável: usar o modo somente visualizar para descobrir quais apps reagem mal a `SW_HIDE`, e adicioná-los só quando o teste mostrar problema.
