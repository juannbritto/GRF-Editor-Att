namespace GRF.Core.SafeSave {
	internal interface ISafeSaveFileSystem {
		bool Exists(string path);
		long Length(string path);
		long AvailableFreeSpace(string directory);
		void DeleteOwnedTemporary(string path);
		void MoveNew(string temporaryPath, string destinationPath);
		string ReplaceExisting(string temporaryPath, string destinationPath, string backupPath);
		SafeSaveReplaceResult ReplaceExistingGuarded(string temporaryPath, string destinationPath,
			string operationBackupPath, bool userBackupRequested);
		void CompleteGuardedReplace(SafeSaveReplaceResult replaceResult);
		string RollbackConcurrentReplacement(string destinationPath, SafeSaveReplaceResult replaceResult);
	}

	internal sealed class SafeSaveReplaceResult {
		internal SafeSaveReplaceResult(string operationBackupPath, string retainedPreviousBackupPath,
			bool userBackupRequested) {
			OperationBackupPath = operationBackupPath;
			RetainedPreviousBackupPath = retainedPreviousBackupPath;
			UserBackupRequested = userBackupRequested;
		}

		internal string OperationBackupPath { get; }
		internal string RetainedPreviousBackupPath { get; private set; }
		internal bool UserBackupRequested { get; }

		internal void PreviousBackupRemoved() {
			RetainedPreviousBackupPath = null;
		}
	}
}
