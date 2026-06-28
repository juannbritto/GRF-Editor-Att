using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GRF.Core.SafeSave {
	internal sealed class SafeSaveFileSystem : ISafeSaveFileSystem {
		private static readonly Regex OwnedTemporaryName = new Regex(
			@"^.+\.safe-save-[0-9a-f]{32}\.tmp$",
			RegexOptions.CultureInvariant);

		public bool Exists(string path) => File.Exists(path);

		public long Length(string path) => new FileInfo(path).Length;

		public long AvailableFreeSpace(string directory) {
			string root = Path.GetPathRoot(Path.GetFullPath(directory));
			return new DriveInfo(root).AvailableFreeSpace;
		}

		public void DeleteOwnedTemporary(string path) {
			if (!OwnedTemporaryName.IsMatch(Path.GetFileName(path))) {
				throw new InvalidOperationException("Only coordinator-owned temporary files may be deleted.");
			}

			File.Delete(path);
		}

		public void MoveNew(string temporaryPath, string destinationPath) {
			File.Move(temporaryPath, destinationPath);
		}

		public void ReplaceExisting(string temporaryPath, string destinationPath, string backupPath) {
			string previousBackupPath = backupPath == null ? null : backupPath + ".previous-safe-save";
			bool previousBackupPreserved = false;

			if (backupPath != null && File.Exists(backupPath)) {
				if (File.Exists(previousBackupPath)) File.Delete(previousBackupPath);
				File.Move(backupPath, previousBackupPath);
				previousBackupPreserved = true;
			}

			try {
				File.Replace(temporaryPath, destinationPath, backupPath, true);
			}
			catch {
				if (previousBackupPreserved) {
					if (File.Exists(backupPath)) File.Delete(backupPath);
					File.Move(previousBackupPath, backupPath);
				}

				throw;
			}

			if (previousBackupPreserved) {
				try {
					File.Delete(previousBackupPath);
				}
				catch (IOException) {
					// Promotion succeeded; stale-backup cleanup cannot roll it back.
				}
				catch (UnauthorizedAccessException) {
					// Promotion succeeded; stale-backup cleanup cannot roll it back.
				}
			}
		}
	}
}
