using System;

namespace GRF.Core.SafeSave {
	public sealed class SafeSaveUiState {
		private SafeSaveUiState(string label, string explanation, bool canExecuteWrite, string reasonCode) {
			Label = label;
			Explanation = explanation;
			CanExecuteWrite = canExecuteWrite;
			ReasonCode = reasonCode;
		}

		public string Label { get; }
		public string Explanation { get; }
		public bool CanExecuteWrite { get; }
		public string ReasonCode { get; }

		public static SafeSaveUiState From(ContainerWriteClassification classification, string cultureName) {
			if (classification == null) throw new ArgumentNullException(nameof(classification));
			return new SafeSaveUiState(
				SafeSaveUiText.Label(classification.Capability, cultureName),
				SafeSaveUiText.Explanation(classification.ReasonCode, cultureName),
				classification.Capability == ContainerWriteCapability.Editable,
				classification.ReasonCode);
		}
	}
}
