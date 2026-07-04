# Validação de compatibilidade do GRF Editor Safe

Data da validação: 3 de julho de 2026.

## Amostra clássica

- Arquivo de origem: `astegrf2024.grf`
- Tamanho: `144420333` bytes
- SHA-256: `a7254c34bd978592b9a2d63523976685d20a56d329ebd97feea8440c62e65446`
- Regra de segurança: o arquivo externo foi somente lido e copiado para
  `artifacts/grf-safe-fixtures`; todas as gravações ocorreram numa segunda cópia
  descartável dentro desse diretório.

O teste adicionou `data\grf_editor_safe_probe.txt`, executou o salvamento seguro,
reabriu a GRF, validou a estrutura e o manifesto lógico completos e confirmou que
o `.bak` era idêntico ao estado anterior ao salvamento. A cópia descartável foi
removida ao final. O hash da origem foi conferido antes e depois e permaneceu igual.

## Ragnarok LATAM

O arquivo `C:\Gravity\Ragnarok\data.grf`, com `4634714923` bytes, não foi copiado
nem aberto para escrita. Somente seu tamanho, data e os primeiros 46 bytes foram
lidos. O cabeçalho começa com `Event Horizon`, classificado pelo editor como formato
protegido e somente leitura. Esta validação **não** afirma compatibilidade de
gravação com a GRF LATAM.

## Comandos de validação

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\prepare-grf-safe-fixtures.ps1 `
  -ClassicSource "C:\Users\juann\Documents\Juan\Programas Rag\GRF's\astegrf2024\grf\astegrf2024.grf" `
  -ProtectedSource 'C:\Gravity\Ragnarok\data.grf'

$env:GRF_REAL_FIXTURE_DIR = (Resolve-Path .\artifacts\grf-safe-fixtures).Path
vstest.console.exe .\GRF.SafeSave.Tests\bin\Debug\net48\GRF.SafeSave.Tests.dll `
  /Platform:x64 /TestCaseFilter:'TestCategory=RealGrf'
```

## Limite restante

A aceitação pelo cliente e pelo patcher ainda precisa ser confirmada iniciando um
cliente compatível com private server/iRO/rAthena/Hercules contra uma cópia
descartável da GRF. Nenhum cliente deve ser apontado para a amostra original durante
essa verificação.

## Validação da interface clássica refinada

- Compilação `Release` concluída para `GRF Editor Safe.exe`.
- Suíte completa: **136 testes aprovados, 0 falhas e 0 ignorados** com a fixture real configurada.
- Janela principal verificada com a área A2 equilibrada, três painéis, pesquisa,
  progresso, estado de compatibilidade e contagem de arquivos.
- Configurações, validação, merge e diálogos de salvamento verificados sem executar
  gravação, merge, validação destrutiva ou qualquer outra alteração de arquivo.
- Ferramentas de mapas, sprites, extração, criptografia, patch, hash, imagem e OpenGL
  receberam apenas acabamento compartilhado; seus renderizadores e algoritmos não
  foram modificados.
- O arquivo original `astegrf2024.grf` permaneceu com SHA-256
  `a7254c34bd978592b9a2d63523976685d20a56d329ebd97feea8440c62e65446`.
