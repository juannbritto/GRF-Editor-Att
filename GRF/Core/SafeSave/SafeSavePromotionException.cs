using System;
using System.IO;

namespace GRF.Core.SafeSave {
	internal sealed class SafeSavePromotionException : IOException {
		internal SafeSavePromotionException(string message, Exception innerException, bool canDeleteTemporary,
			string recoveryCode, string recoveryPath, string retainedPreviousBackupPath = null,
			string actualBackupPath = null) : base(message, innerException) {
			CanDeleteTemporary = canDeleteTemporary;
			RecoveryCode = recoveryCode;
			RecoveryPath = recoveryPath;
			RetainedPreviousBackupPath = retainedPreviousBackupPath;
			ActualBackupPath = actualBackupPath;
		}

		internal bool CanDeleteTemporary { get; }
		internal string RecoveryCode { get; }
		internal string RecoveryPath { get; }
		internal string RetainedPreviousBackupPath { get; }
		internal string ActualBackupPath { get; }
	}
}
