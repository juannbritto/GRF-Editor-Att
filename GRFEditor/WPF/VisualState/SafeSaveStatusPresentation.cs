using System;
using GRF.Core.SafeSave;

namespace GRFEditor.WPF.VisualState {
	public enum SafeSaveStatusKind {
		Waiting,
		Editable,
		Busy,
		Warning,
		ReadOnly
	}

	public sealed class SafeSaveStatusPresentation {
		private SafeSaveStatusPresentation(SafeSaveStatusKind kind, string brushKey, SafeSaveUiState state) {
			Kind = kind;
			BrushKey = brushKey;
			Label = state.Label;
			Explanation = state.Explanation;
			CanExecuteWrite = state.CanExecuteWrite;
		}

		public SafeSaveStatusKind Kind { get; }
		public string BrushKey { get; }
		public string Label { get; }
		public string Explanation { get; }
		public bool CanExecuteWrite { get; }

		public static SafeSaveStatusPresentation From(SafeSaveUiState state) {
			if (state == null) throw new ArgumentNullException(nameof(state));

			return state.CanExecuteWrite
				? new SafeSaveStatusPresentation(SafeSaveStatusKind.Editable, "GrfSuccessBrush", state)
				: new SafeSaveStatusPresentation(SafeSaveStatusKind.ReadOnly, "GrfDangerBrush", state);
		}
	}
}
