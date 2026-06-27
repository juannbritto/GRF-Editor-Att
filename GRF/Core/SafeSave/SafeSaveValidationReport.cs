using System.Collections.Generic;
using System.Linq;

namespace GRF.Core.SafeSave {
	public enum SafeSavePhase {
		Preflight,
		WriteTemporary,
		Validate,
		Backup,
		Promote,
		Confirm
	}

	public enum SafeSaveSeverity {
		Information,
		Warning,
		Error
	}

	public sealed class SafeSaveValidationItem {
		public SafeSaveValidationItem(SafeSavePhase phase, SafeSaveSeverity severity, string code, string path, string message) {
			Phase = phase;
			Severity = severity;
			Code = code;
			Path = path;
			Message = message;
		}

		public SafeSavePhase Phase { get; }
		public SafeSaveSeverity Severity { get; }
		public string Code { get; }
		public string Path { get; }
		public string Message { get; }
	}

	public sealed class SafeSaveValidationReport {
		public List<SafeSaveValidationItem> Items { get; } = new List<SafeSaveValidationItem>();

		public bool HasErrors => Items.Any(item => item.Severity == SafeSaveSeverity.Error);

		public void Add(SafeSavePhase phase, SafeSaveSeverity severity, string code, string path, string message) {
			Items.Add(new SafeSaveValidationItem(phase, severity, code, path, message));
		}

		public override string ToString() {
			return string.Join("\n", Items.Select(item => $"[{item.Severity}] {item.Code}: {item.Message}"));
		}
	}
}
