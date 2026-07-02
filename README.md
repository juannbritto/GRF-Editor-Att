# GRF Editor Att

Atualização comunitária do **GRF Editor**, editor de arquivos GRF, GPF e THOR do
Ragnarok Online. Este projeto mantém compatibilidade com a estrutura clássica dos
arquivos e prioriza operações seguras para ambientes atuais, incluindo Ragnarok
LATAM, iRO e servidores privados baseados em rAthena/Hercules.

O projeto deriva do [GRFEditor original](https://github.com/Tokeiburu/GRFEditor).
O objetivo é modernizar sua base com melhorias graduais, auditáveis e testadas,
preservando o crédito e o histórico do trabalho original.

## Princípios de segurança

- Nunca editar uma GRF de referência ou de instalação diretamente durante testes.
- Trabalhar em cópias e promover o resultado somente após validação completa.
- Preservar cabeçalho, tabela de arquivos, codificação de nomes e regras esperadas
  pelos clientes de Ragnarok.
- Bloquear gravação em formatos desconhecidos, protegidos ou não suportados.
- Manter artefatos de recuperação quando uma substituição não puder ser concluída.

## Mudanças desta atualização

- Salvamento transacional: a nova GRF é escrita e validada em arquivo temporário
  antes de substituir o destino.
- Substituição atômica com backup e rollback, reduzindo o risco de corromper uma
  GRF caso o processo seja interrompido ou o disco apresente erro.
- Restauração validada de backups sem consumir o `.bak`, com ponto de restauração
  mantido até a confirmação estrutural da GRF recuperada.
- Descoberta restrita de temporários pertencentes ao salvamento seguro, evitando
  confundir ou remover arquivos semelhantes criados pelo usuário.
- Estado de compatibilidade visível na interface, fases de salvamento apresentadas
  durante a operação e comandos para consultar o último relatório de validação.
- Preferências para criação de `.bak` e nível de detalhes do relatório; a validação
  estrutural permanece obrigatória independentemente dessas preferências.
- Recuperação pela interface limitada ao backup padrão e a temporários reconhecidos,
  sem promoção automática de arquivos abandonados.
- Proteção contra alteração concorrente: identidade do arquivo, tamanho, data,
  cabeçalho e política de formato são conferidos novamente no momento da troca.
- Validação estrutural de GRF/GPF e dos contêineres auxiliares usados por THOR.
- Preservação do estado interno do editor quando uma gravação falha.
- Tratamento seguro do índice `files.enc` e dos fluxos de criptografia existentes.
- Limite configurável de memória para entradas grandes, evitando consumo excessivo
  durante validação e salvamento.
- Remoção de exclusões antecipadas no editor de mapas; os arquivos só são trocados
  após a nova saída estar pronta.
- Cobertura automatizada para salvamento, rollback, formatos protegidos, alteração
  concorrente e integração com `GrfHolder`.

Essas mudanças existem porque o fluxo antigo podia modificar o destino durante a
gravação e deixá-lo incompleto em caso de falha. A nova abordagem trata a GRF como
um artefato imutável até que a saída substituta tenha sido verificada.

## Estado e compatibilidade

Esta atualização ainda está em desenvolvimento. Antes de usar uma versão em uma
instalação real, faça backup e valide a cópia no cliente/servidor desejado. Arquivos
Event Horizon, formatos desconhecidos e variantes sem suporte permanecem somente
para leitura por segurança.

A distribuição lado a lado usa o nome **GRF Editor Safe**, versão `1.6.0.0`. Ela
possui executável, pasta de configuração, atalhos e desinstalador próprios. O
instalador não assume associações de arquivos do GRF Editor existente.

## GRF library (documentação original)

An editor for the GRF/GPF/Thor file formats from Ragnarok Online.

### Using the GRF library
#### Setting up the ErrorHandler
With a new project, the first thing you'll want to do is look into the ErrorHandler.
By default, the ErrorHandler is set to use ErrorManager.DefaultHandler which uses the Console window and may not be very helpful.
GRF Editor uses GrfToWpfBridge.Application.DefaultErrorHandler which uses a WPF based interface.
If you want to handle the exceptions yourself, you can simply rethrow them with the RethrowErrorHandler shown below (or make your own).
```
public class RethrowErrorHandler : IErrorHandler {
	public void Handle(Exception exception, ErrorLevel errorLevel) {
		throw exception;
	}

	public bool YesNoRequest(string message, string caption) {
		if (MessageBox.Show("The application requires your attention.\n\n" + message, caption, MessageBoxButtons.YesNo) == DialogResult.Yes)
			return true;

		return false;
	}
}

public class GrfTest {
	public GrfTest() {
		ErrorHandler.SetErrorHandler(new DefaultHandler());
		ErrorHandler.SetErrorHandler(new RethrowErrorHandler());
		ErrorHandler.SetErrorHandler(new DefaultErrorHandler());
	}
}
```
#### Using GrfHolder
The GrfHolder is the main class for handling GRF files. All operations on the GRF must be applied by using the Commands object. You can find all the available methods in GRF.ContainerFormat.Commands.CommandsHolder.cs.
```
// Add and remove files
GrfHolder grf = new GrfHolder(@"C:\data.grf");

grf.Commands.AddFile(@"data\texture\grid.tga", @"C:\test\custom_grid.tga");
grf.Commands.RemoveFile(@"data\texture\loading00.jpg");

if (grf.Commands.CanUndo)
	grf.Commands.Undo();

if (grf.Commands.CanRedo)
	grf.Commands.Redo();

grf.QuickSave();
// Reload is only necessary if you plan on using the GRF again after saving it.
grf.Reload();
```

The entries from the GRF are stored in the FileTable object and can be extracted as follows:
```
GrfHolder grf = new GrfHolder(@"C:\data.grf");

var entry = grf.FileTable.TryGet(@"data\texture\grid.tga");

if (entry == null)
	throw new Exception("Entry not found.");

// You can also get the entry directly as follow
entry = grf.FileTable[@"data\texture\grid.tga"];

var data = entry.GetDecompressedData();

File.WriteAllBytes(@"C:\test\custom_grid.tga", data);
```

You can also iterate through a specific folder in the GRF.
```
GrfHolder grf = new GrfHolder(@"C:\data.grf");

foreach (var entry in grf.FileTable.EntriesInDirectory(@"C:\texture", SearchOption.TopDirectoryOnly)) {
	if (entry.RelativePath.IsExtension(".bmp")) {
		// Using GrfImage requires additional configuration, more on that later.
		GrfImage image = new GrfImage(entry);

		image.Convert(GrfImageType.Bgra32);
		image.Save(GrfPath.Combine(@"C:\test\", entry.RelativePath));
	}
}
```
