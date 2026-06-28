using System;

namespace GRF.Core.SafeSave {
	public sealed class SafeSaveOptions {
		public bool CreateBackup { get; set; } = true;
		public bool IncludeInformationItems { get; set; } = true;
		public string BackupSuffix { get; set; } = ".bak";
		public Action<SafeSavePhase> PhaseChanged { get; set; }
	}
}
