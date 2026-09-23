# LogViewer — Roadmap (pós-Fase 9)

Planeamento das melhorias e funcionalidades identificadas na revisão de setembro de 2026 que **não** entraram
na Fase 9. A Fase 9 (ver `PLAN.md`) implementou: navegador do ficheiro completo com índice de linhas, navegação
temporal (Δ, ir para hora, filtro por intervalo), formatos personalizados por regex, e filtro por ID de
correlação + agrupamento de exceções.

Prioridade: **P1** = maior impacto / custo moderado, **P2** = útil, **P3** = oportunista.
Esforço: **S** ≈ ≤1 dia, **M** ≈ 2–4 dias, **L** ≈ 1 semana+. ✅ = feito (Fases 10–13, ver `PLAN.md`).

---

## 1. Qualidade e verificação

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 1.1 | ✅ Correr `LogViewer.App.Tests` no CI (`windows-latest`) | P1 | S | Hoje o CI só corre Core e Mcp; os testes de ViewModel são rápidos (~6 s) e apanham regressões de UI lógica. |
| 1.2 | Passagem manual interativa às Fases 7–9 | P1 | S | **Parcial:** submenu de correlação e popup 🕐 agora cobertos por testes FlaUI (encontrou e corrigiu um bug: `_` tratado como tecla de acesso); pt-PT verificado por capturas (popup 🕐, painel de exceções). Falta: toast de alerta real, SSH/ETW contra host real. |
| 1.3 | ✅ Testes FlaUI para os diálogos da Fase 7 (Compare Files, Stats) | P2 | S | Mesmo padrão de `Phase9UITests` (procurar janelas com owner nos descendentes do desktop). |
| 1.4 | ✅ Teste de carga do navegador do ficheiro completo com ficheiro de vários GB | P2 | S | Medir tempo de indexação e memória; benchmark em `benchmarks/` para `FileLineIndex.UpdateAsync`/`ReadLines`. |

## 2. Desempenho e escala

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 2.1 | ✅ Persistir o índice de linhas em disco (cache por caminho + tamanho + mtime) | P2 | M | Evita reindexar ficheiros enormes a cada abertura do navegador; invalidar se o ficheiro encolher ou o início mudar. |
| 2.2 | ✅ Pesquisa no navegador do ficheiro completo | P1 | M | Reutilizar `FileFullTextSearchService` com resultados incrementais e barra de progresso; "seguinte/anterior" salta no `VirtualFileLineList`. |
| 2.3 | Filtros no navegador do ficheiro completo | P2 | L | Pede um índice de "linhas que passam o filtro" construído em background (lista de números de linha) em vez de filtrar a vista. |
| 2.4 | Índice partilhado entre documento e navegador | P3 | M | O `FileTailSource` já conhece offsets: poderia alimentar o índice ao ler, dispensando o scan inicial para ficheiros abertos desde o início. |

## 3. UX

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 3.1 | ✅ Marcadores na barra de scroll (erros, avisos, bookmarks, realces, novos padrões) | P1 | M | Faixa própria ao lado da lista (`ScrollMarkerStrip`). Resultados da pesquisa ainda não aparecem na faixa. |
| 3.2 | ✅ Vistas de filtro nomeadas, reutilizáveis entre documentos | P2 | M | Guardar combinações (texto + nível + intervalo + correlação) com nome; aplicar a qualquer documento. Hoje persistem só por documento/perfil. Bump de schema. |
| 3.3 | ✅ Persistir filtro de tempo e de correlação nos perfis de sessão | P3 | S | Hoje são ad-hoc (não persistidos). Bump de schema em `TailSourceSettings`. |
| 3.4 | Agrupar entradas multi-linha na vista principal | P2 | L | Tratar stack traces como uma única entrada (expandir/colapsar) usando a mesma deteção do `ExceptionGrouper`. |
| 3.5 | ✅ Continuação multi-linha nos formatos personalizados | P2 | M | Opção "linhas que não correspondem pertencem à entrada anterior" no `CustomLogFormat`. |
| 3.6 | ✅ Espaço vazio de ~220 px por baixo do documento | P2 | S | Causa: sem linha selecionada, o conversor da altura do painel de detalhe recebia `UnsetValue` (não `null`) e reservava 220 px. |
| 3.7 | ✅ Caixa de filtro de texto invisível na barra do documento | P2 | S | Estilo explícito `ToolbarTextBoxStyle` (o `ToolBar` impõe um estilo sem borda). |
| 3.8 | ✅ Resultados de pesquisa na faixa de marcadores | P3 | S | Complemento do 3.1. |

## 4. Análise

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 4.1 | ✅ Deteção de anomalias | P1 | M | Assinalar padrões (`IPatternFrequencyAnalyzer`) vistos pela primeira vez e picos de volume face à média móvel; integrar com a timeline (barra destacada) e com os alertas existentes. |
| 4.2 | ✅ Correlação entre documentos | P2 | M | "Filtrar por este ID em todos os documentos abertos" ou abrir uma vista merged filtrada pelo ID. |
| 4.3 | ✅ Painel de exceções em tempo real | P3 | S | Atualizar grupos à medida que chegam linhas (hoje é um snapshot com "Atualizar"). |
| 4.4 | Vista em colunas para logs estruturados | P2 | L | Grelha com colunas escolhidas das propriedades, ordenável e filtrável por valor. |

## 5. Formatos e fontes

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 5.1 | ✅ Arquivos `.zip`, `.bz2`, `.zst` | P2 | M | Mesmo padrão do `CompressedLogFile` (descompactar para temp). `.zip` com vários ficheiros pede escolha. |
| 5.2 | ✅ Diálogo dedicado para `kubectl logs -f` / `docker logs -f` | P2 | M | O `ProcessTailSource` já funciona; falta UX para escolher contexto/namespace/pod/container. |
| 5.3 | EventLog remoto via WinRM | P3 | L | Adiado na Fase 7 (não existe cliente WS-Man leve para .NET). |

## 6. MCP

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 6.1 | ✅ `logs_get_bookmarks`, `logs_query_time_range` | P1 | S | O intervalo de tempo pode usar `FileLineIndex.FindFirstLineAtOrAfter` — barato mesmo em ficheiros enormes. Sempre com `ResponseLimits`. |
| 6.2 | ✅ `logs_get_new_lines_since(cursor)` | P2 | M | Permite ao agente acompanhar o tail sem reler; o cursor é o número de linha absoluto. |
| 6.3 | ✅ `logs_get_alerts` | P3 | S | Expor os disparos do `AlertWindowTracker`. |
| 6.4 | Escrita controlada: adicionar bookmarks/anotações | P3 | M | Opt-in separado nas definições do MCP; nunca modificar ficheiros. |

## 7. Colaboração

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 7.1 | ✅ Anotações/notas em linhas | P2 | M | Persistidas por ficheiro (caminho + nº de linha + hash do texto para detetar mudanças). |
| 7.2 | Relatório de incidente | P3 | M | Exportar excerto (linhas selecionadas/bookmarks + anotações + grupos de exceções) para HTML ou Markdown. |

---

## Ordem sugerida

Feitos: todos os P1 (Fase 10); 2.1, 3.2, 3.3, 3.6, 3.7, 4.2, 6.2 (Fase 11); 3.5, 3.8, 4.3, 5.1, 5.2 (Fase 12);
1.3, 1.4, 6.3, 7.1 (Fase 13). Falta do 1.2 o toast real, SSH/ETW contra hosts reais e o diálogo de contentores contra
um Docker/Kubernetes real (nenhum dos dois está instalado na máquina de desenvolvimento). A seguir:

1. **7.2 + 6.4** — relatório de incidente (bookmarks + notas + grupos de exceções → Markdown/HTML) e escrita
   controlada de bookmarks/notas pelo MCP; as notas (7.1) são a base de ambos.
2. **3.4 / 4.4** — entradas multi-linha agrupadas na vista (base já existe com o 3.5) e vista em colunas (maiores).
3. **2.3, 2.4, 5.3** — conforme a necessidade.
