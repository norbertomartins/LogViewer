# LogViewer — Roadmap (pós-Fase 9)

Planeamento das melhorias e funcionalidades identificadas na revisão de setembro de 2026 que **não** entraram
na Fase 9. A Fase 9 (ver `PLAN.md`) implementou: navegador do ficheiro completo com índice de linhas, navegação
temporal (Δ, ir para hora, filtro por intervalo), formatos personalizados por regex, e filtro por ID de
correlação + agrupamento de exceções.

Prioridade: **P1** = maior impacto / custo moderado, **P2** = útil, **P3** = oportunista.
Esforço: **S** ≈ ≤1 dia, **M** ≈ 2–4 dias, **L** ≈ 1 semana+.

---

## 1. Qualidade e verificação

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 1.1 | Correr `LogViewer.App.Tests` no CI (`windows-latest`) | P1 | S | Hoje o CI só corre Core e Mcp; os testes de ViewModel são rápidos (~6 s) e apanham regressões de UI lógica. |
| 1.2 | Passagem manual interativa às Fases 7–9 | P1 | S | Toast de alerta real, submenu "Filtrar por ID de correlação", popup 🕐, strings pt-PT (ajuste de layout), SSH/ETW contra host real. |
| 1.3 | Testes FlaUI para os diálogos da Fase 7 (Compare Files, Stats) | P2 | S | Mesmo padrão de `Phase9UITests` (procurar janelas com owner nos descendentes do desktop). |
| 1.4 | Teste de carga do navegador do ficheiro completo com ficheiro de vários GB | P2 | S | Medir tempo de indexação e memória; benchmark em `benchmarks/` para `FileLineIndex.UpdateAsync`/`ReadLines`. |

## 2. Desempenho e escala

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 2.1 | Persistir o índice de linhas em disco (cache por caminho + tamanho + mtime) | P2 | M | Evita reindexar ficheiros enormes a cada abertura do navegador; invalidar se o ficheiro encolher ou o início mudar. |
| 2.2 | Pesquisa no navegador do ficheiro completo | P1 | M | Reutilizar `FileFullTextSearchService` com resultados incrementais e barra de progresso; "seguinte/anterior" salta no `VirtualFileLineList`. |
| 2.3 | Filtros no navegador do ficheiro completo | P2 | L | Pede um índice de "linhas que passam o filtro" construído em background (lista de números de linha) em vez de filtrar a vista. |
| 2.4 | Índice partilhado entre documento e navegador | P3 | M | O `FileTailSource` já conhece offsets: poderia alimentar o índice ao ler, dispensando o scan inicial para ficheiros abertos desde o início. |

## 3. UX

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 3.1 | Marcadores na barra de scroll (erros, bookmarks, resultados de pesquisa, realces) | P1 | M | Adorner sobre a `ScrollBar` do `LineListView`; cores do tema. |
| 3.2 | Vistas de filtro nomeadas, reutilizáveis entre documentos | P2 | M | Guardar combinações (texto + nível + intervalo + correlação) com nome; aplicar a qualquer documento. Hoje persistem só por documento/perfil. Bump de schema. |
| 3.3 | Persistir filtro de tempo e de correlação nos perfis de sessão | P3 | S | Hoje são ad-hoc (não persistidos). Bump de schema em `TailSourceSettings`. |
| 3.4 | Agrupar entradas multi-linha na vista principal | P2 | L | Tratar stack traces como uma única entrada (expandir/colapsar) usando a mesma deteção do `ExceptionGrouper`. |
| 3.5 | Continuação multi-linha nos formatos personalizados | P2 | M | Opção "linhas que não correspondem pertencem à entrada anterior" no `CustomLogFormat`. |

## 4. Análise

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 4.1 | Deteção de anomalias | P1 | M | Assinalar padrões (`IPatternFrequencyAnalyzer`) vistos pela primeira vez e picos de volume face à média móvel; integrar com a timeline (barra destacada) e com os alertas existentes. |
| 4.2 | Correlação entre documentos | P2 | M | "Filtrar por este ID em todos os documentos abertos" ou abrir uma vista merged filtrada pelo ID. |
| 4.3 | Painel de exceções em tempo real | P3 | S | Atualizar grupos à medida que chegam linhas (hoje é um snapshot com "Atualizar"). |
| 4.4 | Vista em colunas para logs estruturados | P2 | L | Grelha com colunas escolhidas das propriedades, ordenável e filtrável por valor. |

## 5. Formatos e fontes

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 5.1 | Arquivos `.zip`, `.bz2`, `.zst` | P2 | M | Mesmo padrão do `CompressedLogFile` (descompactar para temp). `.zip` com vários ficheiros pede escolha. |
| 5.2 | Diálogo dedicado para `kubectl logs -f` / `docker logs -f` | P2 | M | O `ProcessTailSource` já funciona; falta UX para escolher contexto/namespace/pod/container. |
| 5.3 | EventLog remoto via WinRM | P3 | L | Adiado na Fase 7 (não existe cliente WS-Man leve para .NET). |

## 6. MCP

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 6.1 | `logs_get_bookmarks`, `logs_query_time_range` | P1 | S | O intervalo de tempo pode usar `FileLineIndex.FindFirstLineAtOrAfter` — barato mesmo em ficheiros enormes. Sempre com `ResponseLimits`. |
| 6.2 | `logs_get_new_lines_since(cursor)` | P2 | M | Permite ao agente acompanhar o tail sem reler; o cursor é o número de linha absoluto. |
| 6.3 | `logs_get_alerts` | P3 | S | Expor os disparos do `AlertWindowTracker`. |
| 6.4 | Escrita controlada: adicionar bookmarks/anotações | P3 | M | Opt-in separado nas definições do MCP; nunca modificar ficheiros. |

## 7. Colaboração

| # | Item | Prio | Esforço | Notas |
|---|------|------|---------|-------|
| 7.1 | Anotações/notas em linhas | P2 | M | Persistidas por ficheiro (caminho + nº de linha + hash do texto para detetar mudanças). |
| 7.2 | Relatório de incidente | P3 | M | Exportar excerto (linhas selecionadas/bookmarks + anotações + grupos de exceções) para HTML ou Markdown. |

---

## Ordem sugerida

1. **1.1 + 1.2** — fechar a verificação do que já existe.
2. **2.2 + 3.1** — pesquisa no navegador e marcadores na scrollbar tornam o índice realmente útil no dia-a-dia.
3. **4.1** — anomalias, sobre a timeline e os alertas existentes.
4. **6.1** — ferramentas MCP baratas que reaproveitam o índice.
5. O resto conforme a necessidade.
