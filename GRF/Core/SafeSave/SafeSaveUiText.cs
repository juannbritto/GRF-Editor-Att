using System;

namespace GRF.Core.SafeSave {
	public static class SafeSaveUiText {
		public static bool IsPortuguese(string cultureName) {
			return !string.IsNullOrEmpty(cultureName) && cultureName.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
		}

		public static string Label(ContainerWriteCapability capability, string cultureName) {
			bool portuguese = IsPortuguese(cultureName);
			switch (capability) {
				case ContainerWriteCapability.Editable:
					return portuguese ? "Editável" : "Editable";
				case ContainerWriteCapability.ReadOnlyProtected:
					return portuguese ? "Somente leitura — formato protegido" : "Read-only — protected format";
				default:
					return portuguese ? "Somente leitura — formato desconhecido" : "Read-only — unknown format";
			}
		}

		public static string Explanation(string reasonCode, string cultureName) {
			bool portuguese = IsPortuguese(cultureName);
			switch (reasonCode) {
				case "classic-supported":
					return portuguese ? "Formato GRF clássico compatível com gravação segura." : "Classic GRF format supported for safe writing.";
				case "event-horizon":
					return portuguese ? "O formato Event Horizon é protegido e permanece somente para leitura." : "The Event Horizon format is protected and remains read-only.";
				case "unsupported-version":
					return portuguese ? "Esta versão do formato não possui suporte seguro para gravação." : "This format version is not supported for safe writing.";
				case "header-errors":
					return portuguese ? "O cabeçalho contém erros e não pode ser gravado com segurança." : "The header contains errors and cannot be written safely.";
				default:
					return portuguese ? "O formato não foi reconhecido e permanece somente para leitura." : "The format was not recognized and remains read-only.";
			}
		}

		public static string Phase(SafeSavePhase phase, string cultureName) {
			bool portuguese = IsPortuguese(cultureName);
			switch (phase) {
				case SafeSavePhase.Preflight: return portuguese ? "Verificando destino" : "Checking destination";
				case SafeSavePhase.WriteTemporary: return portuguese ? "Gravando arquivo temporário" : "Writing temporary file";
				case SafeSavePhase.Validate: return portuguese ? "Validando estrutura" : "Validating structure";
				case SafeSavePhase.Backup: return portuguese ? "Criando backup" : "Creating backup";
				case SafeSavePhase.Promote: return portuguese ? "Promovendo arquivo validado" : "Promoting validated file";
				default: return portuguese ? "Confirmando resultado" : "Confirming result";
			}
		}
	}
}
