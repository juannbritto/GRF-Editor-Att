# GRF Editor Safe — Design de salvamento compatível

## Objetivo

Criar uma variante do GRF Editor instalada lado a lado, chamada **GRF Editor Safe**, cuja primeira entrega priorize a integridade de arquivos GRF clássicos usados por private servers, iRO e clientes associados a rAthena/Hercules. A aplicação deve impedir que uma gravação incompleta, estruturalmente inválida ou de formato não suportado substitua um arquivo utilizável.

O projeto parte do repositório oficial `Tokeiburu/GRFEditor` e preserva seu leitor e gravador existentes. A primeira entrega adiciona uma camada de segurança ao fluxo de salvamento; não reescreve o formato GRF nem moderniza toda a interface.

## Compatibilidade e limites

- GRFs com o cabeçalho clássico `Master of Magic` permanecem editáveis quando a versão e a tabela de arquivos forem reconhecidas pelo leitor existente.
- A versão, o cabeçalho, as flags de entrada e o método de compressão devem ser preservados, salvo quando uma ação explícita do usuário exigir uma mudança suportada.
- Arquivos com cabeçalho `Event Horizon`, incluindo a `data.grf` observada na instalação do Ragnarok LATAM, serão abertos somente para identificação e leitura suportada. Toda operação de escrita ficará bloqueada.
- Cabeçalhos, versões ou variantes desconhecidas serão somente leitura.
- O recurso não tenta remover, contornar ou reproduzir proteções da Gravity, de patchers ou de sistemas anti-cheat.
- Servidores rAthena/Hercules normalmente não leem a GRF do cliente diretamente. A compatibilidade aqui significa produzir arquivos que clientes e patchers compatíveis com o formato clássico consigam consumir sem corrupção estrutural.

## Arquitetura

### 1. Classificação de capacidade

Ao abrir um contêiner, uma política de capacidade o classifica como:

- `Editable`: formato clássico reconhecido e gravável;
- `ReadOnlyProtected`: formato protegido conhecido, como `Event Horizon`;
- `ReadOnlyUnknown`: cabeçalho ou versão desconhecida.

A classificação é calculada no núcleo, não apenas na interface. Todos os pontos de entrada de escrita consultam a mesma política, incluindo menus, atalhos e chamadas de salvamento.

### 2. Coordenador de salvamento seguro

O comando de salvamento passa por um coordenador com estas fases:

1. Verificar se o formato é gravável e se há espaço livre suficiente para o temporário e o backup.
2. Registrar uma fotografia lógica da origem: caminho, tamanho, cabeçalho, versão e manifesto das entradas.
3. Gravar as mudanças em um arquivo temporário no mesmo volume do destino.
4. Fechar e reabrir o temporário por um novo leitor.
5. Executar a validação estrutural e de conteúdo.
6. Criar ou atualizar o backup `arquivo.grf.bak` quando o destino já existir.
7. Promover o temporário por substituição atômica no mesmo volume.
8. Reabrir o destino promovido e confirmar que ele corresponde ao resultado validado.

O fluxo “Salvar como” utiliza as mesmas etapas, mas não cria backup quando o caminho de destino ainda não existe. A política será configurável, com substituição segura e backup habilitados por padrão.

### 3. Validador pós-gravação

O validador produz um relatório com erros, avisos e informações. A promoção é permitida somente quando não há erros.

Validações obrigatórias:

- assinatura, versão e campos do cabeçalho;
- limites e consistência da tabela de arquivos;
- offsets sem valores negativos, sobreposição inválida ou acesso além do fim do arquivo;
- tamanhos comprimido, alinhado e descomprimido coerentes;
- descompressão integral de cada entrada adicionada ou modificada;
- comparação byte a byte do conteúdo descomprimido esperado para entradas modificadas;
- comparação do conteúdo lógico das entradas não modificadas com o manifesto da origem;
- caminhos válidos para a codificação usada pelo formato e ausência de duplicatas incompatíveis;
- CRC ou checksum quando o formato ou a entrada o fornecer.

Para reduzir o custo em arquivos grandes, entradas não modificadas podem ser validadas por streaming e hash lógico, sem carregar o arquivo inteiro em memória. A primeira versão privilegia correção; otimizações não podem reduzir a cobertura obrigatória.

### 4. Backup e recuperação

- O backup fica ao lado do destino com sufixo `.bak`.
- Um backup existente só é substituído depois que o novo temporário passa na validação.
- “Restaurar backup” valida o `.bak` antes de qualquer substituição e preserva o arquivo atual como temporário de recuperação durante a operação.
- Falhas de I/O, falta de espaço, cancelamento, compressão, validação ou promoção mantêm o original inalterado.
- Temporários abandonados são identificados na próxima abertura e oferecidos apenas para inspeção ou remoção; nunca são promovidos automaticamente.

## Interface

A aplicação será identificada como **GRF Editor Safe** e instalada separadamente do GRF Editor atual.

A janela principal exibirá o estado `Editável`, `Somente leitura — formato protegido` ou `Somente leitura — formato desconhecido`. Ações incompatíveis serão desabilitadas e também rejeitadas pelo núcleo.

O salvamento exibirá progresso separado para gravação, validação e promoção. Ao final, o usuário poderá abrir um relatório com as verificações executadas. Haverá comandos para “Salvar com segurança”, “Salvar como” e “Restaurar backup”, além de configurações para backup e nível de detalhes do relatório. As novas mensagens serão disponibilizadas em português e inglês.

## Tratamento de erros

Erros são associados à fase em que ocorreram e incluem caminho afetado, operação e causa técnica sem expor chaves ou dados sensíveis. O coordenador realiza limpeza apenas de temporários que ele próprio criou. Ele nunca apaga a origem nem o backup como resposta genérica a uma exceção.

Se a substituição atômica não for suportada pelo sistema de arquivos, o salvamento é interrompido com instrução para usar um destino local compatível; não haverá fallback silencioso para cópia destrutiva.

## Estratégia de testes

Os testes automatizados serão escritos antes da implementação de cada comportamento e usarão:

- GRFs sintéticas pequenas para casos determinísticos;
- uma cópia da amostra `astegrf2024.grf` para testes de integração de escrita;
- leitura limitada da `C:\Gravity\Ragnarok\data.grf` apenas para confirmar a classificação `Event Horizon` como somente leitura.

As amostras originais fora do workspace serão protegidas por hash calculado antes e depois dos testes. Nenhum teste recebe o caminho original como destino de escrita.

A suíte cobrirá abertura e extração antes/depois, caminhos CP949, arquivos vazios, compressão, adição, substituição, renomeação, exclusão, limites de offsets, interrupção em cada fase, falta de espaço simulada, backup, restauração e bloqueio de formatos protegidos ou desconhecidos.

O teste de integração confirma que o conteúdo lógico de todas as entradas não modificadas permanece igual e que as alterações solicitadas sobrevivem a uma nova abertura. Testes de falha confirmam que o hash do original e do backup esperado não muda.

## Entrega e instalação

A saída terá nome, identidade visual e diretório de instalação distintos. A instalação existente em `C:\Program Files (x86)\GRF Editor` não será alterada. A primeira distribuição será considerada experimental até passar pela suíte automatizada e pela validação manual sobre cópias das amostras clássicas.

## Fora do escopo da primeira entrega

- escrita em `Event Horizon` ou outras proteções modernas da Gravity;
- alteração de criptografia para evitar detecção por servidor, patcher ou anti-cheat;
- reescrita completa em .NET moderno;
- redesenho integral da interface;
- otimizações amplas de abertura, pesquisa ou extração que não sejam necessárias ao salvamento seguro;
- edição direta de qualquer GRF instalada ou armazenada fora do workspace.
