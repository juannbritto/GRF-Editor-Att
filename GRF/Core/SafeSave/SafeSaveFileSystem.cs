using System;
using System.IO;
using System.Text.RegularExpressions;

namespace GRF.Core.SafeSave {
	internal sealed class SafeSaveFileSystem : ISafeSaveFileSystem {
		private static readonly Regex OwnedTemporaryName = new Regex(
			@"^.+\.safe-save-[0-9a-f]{32}\.tmp$",
			RegexOptions.CultureInvariant);
		private readonly Action<string, string, string, bool> _replace;
		private readonly Action<string, string, bool> _copy;
		private readonly Action<string, string> _move;

		public SafeSaveFileSystem() : this(File.Replace, File.Copy, File.Move) {
		}

		internal SafeSaveFileSystem(Action<string, string, string, bool> replace) : this(replace, File.Copy, File.Move) {
		}

		internal SafeSaveFileSystem(Action<string, string, string, bool> replace, Action<string, string, bool> copy)
			: this(replace, copy, File.Move) {
		}

		internal SafeSaveFileSystem(Action<string, string, string, bool> replace, Action<string, string, bool> copy,
			Action<string, string> move) {
			_replace = replace ?? throw new ArgumentNullException(nameof(replace));
			_copy = copy ?? throw new ArgumentNullException(nameof(copy));
			_move = move ?? throw new ArgumentNullException(nameof(move));
		}

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
			try {
				_move(temporaryPath, destinationPath);
			}
			catch (Exception exception) {
				bool temporaryExists = File.Exists(temporaryPath);
				bool destinationExists = File.Exists(destinationPath);
				throw new SafeSavePromotionException(
					"Moving the validated temporary to a new destination had an ambiguous result.", exception,
					!temporaryExists && destinationExists, "promotion.move-ambiguous",
					temporaryExists ? temporaryPath : destinationPath);
			}
		}

		public string ReplaceExisting(string temporaryPath, string destinationPath, string backupPath) {
			string previousBackupPath = backupPath == null
				? null
				: backupPath + ".previous-safe-save-" + Guid.NewGuid().ToString("N");
			bool previousBackupPreserved = false;

			if (backupPath != null && File.Exists(backupPath)) {
				File.Move(backupPath, previousBackupPath);
				previousBackupPreserved = true;
			}

			try {
				_replace(temporaryPath, destinationPath, backupPath, true);
			}
			catch (Exception replaceException) {
				bool destinationExists = File.Exists(destinationPath);
				bool temporaryExists = File.Exists(temporaryPath);
				bool currentBackupExists = backupPath != null && File.Exists(backupPath);
				bool canDeleteTemporary = destinationExists;
				string recoveryCode = "promotion.destination-intact";
				string recoveryPath = destinationPath;
				string recoveryMessage = "Atomic replacement failed; recovery state was reconciled.";
				bool consumedCurrentBackup = false;

				if (!destinationExists && currentBackupExists) {
					try {
						File.Move(backupPath, destinationPath);
						destinationExists = true;
						consumedCurrentBackup = true;
						canDeleteTemporary = false;
						recoveryCode = "promotion.restored-backup";
						recoveryPath = temporaryExists ? temporaryPath : backupPath;

						string recreationPath = backupPath + ".backup-recreate-safe-save-" + Guid.NewGuid().ToString("N") + ".tmp";
						try {
							_copy(destinationPath, recreationPath, false);
							File.Move(recreationPath, backupPath);
						}
						catch (Exception recreationException) {
							try {
								if (File.Exists(recreationPath)) File.Delete(recreationPath);
							}
							catch (IOException) {
								// A uniquely named partial sidecar is safer than touching the restored destination.
							}
							catch (UnauthorizedAccessException) {
								// A uniquely named partial sidecar is safer than touching the restored destination.
							}
							recoveryCode = "promotion.restored-backup-recreation-failed";
							recoveryMessage = "The destination was atomically restored, but its backup could not be recreated: " + recreationException.Message;
						}
					}
					catch (Exception recoveryException) {
						canDeleteTemporary = false;
						recoveryCode = "promotion.recovery-failed";
						recoveryPath = temporaryExists ? temporaryPath : backupPath;
						recoveryMessage = "Replacement failed and the backup could not be restored: " + recoveryException.Message;
					}
				}
				else if (!destinationExists && temporaryExists) {
					try {
						File.Move(temporaryPath, destinationPath);
						destinationExists = true;
						temporaryExists = false;
						canDeleteTemporary = true;
						recoveryCode = "promotion.recovered-replacement";
						recoveryPath = destinationPath;
					}
					catch (Exception recoveryException) {
						canDeleteTemporary = false;
						recoveryCode = "promotion.recovery-failed";
						recoveryPath = temporaryPath;
						recoveryMessage = "Replacement failed and the validated temporary could not be restored: " + recoveryException.Message;
					}
				}
				else if (!destinationExists) {
					canDeleteTemporary = false;
					recoveryCode = "promotion.recovery-missing";
					recoveryPath = backupPath ?? destinationPath;
				}

				if (previousBackupPreserved && !consumedCurrentBackup && !File.Exists(backupPath)) {
					try {
						File.Move(previousBackupPath, backupPath);
					}
					catch (IOException) {
						// Keep the uniquely named previous backup for manual recovery.
					}
					catch (UnauthorizedAccessException) {
						// Keep the uniquely named previous backup for manual recovery.
					}
				}
				string retainedPreviousBackupPath = previousBackupPreserved && File.Exists(previousBackupPath)
					? previousBackupPath
					: null;

				string actualBackupPath = backupPath != null && File.Exists(backupPath) ? backupPath : null;
				throw new SafeSavePromotionException(
					recoveryMessage, replaceException, canDeleteTemporary, recoveryCode, recoveryPath,
					retainedPreviousBackupPath, actualBackupPath);
			}

			if (previousBackupPreserved) {
				try {
					File.Delete(previousBackupPath);
				}
				catch (IOException) {
					// Promotion succeeded; stale-backup cleanup cannot roll it back.
					return previousBackupPath;
				}
				catch (UnauthorizedAccessException) {
					// Promotion succeeded; stale-backup cleanup cannot roll it back.
					return previousBackupPath;
				}
			}

			return null;
		}
	}
}
