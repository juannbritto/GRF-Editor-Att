using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GRF.Core.SafeSave {
	public sealed class SafeSaveRecoveryService {
		private readonly Func<string, SafeSaveManifest, SafeSaveValidationReport> _validator;

		public SafeSaveRecoveryService() : this((path, manifest) => new SafeGrfValidator().Validate(path, manifest)) {
		}

		internal SafeSaveRecoveryService(Func<string, SafeSaveManifest, SafeSaveValidationReport> validator) {
			_validator = validator ?? throw new ArgumentNullException(nameof(validator));
		}

		public IEnumerable<string> FindOwnedTemporaries(string destination) {
			if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("A destination path is required.", nameof(destination));

			string fullDestination = Path.GetFullPath(destination);
			string directory = Path.GetDirectoryName(fullDestination);
			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return Enumerable.Empty<string>();

			string fileName = Path.GetFileName(fullDestination);
			var pattern = new Regex("^" + Regex.Escape(fileName) + @"\.safe-save-[0-9a-f]{32}\.tmp$", RegexOptions.CultureInvariant);
			return Directory.EnumerateFiles(directory)
				.Where(path => pattern.IsMatch(Path.GetFileName(path)))
				.OrderBy(path => path, StringComparer.Ordinal)
				.ToArray();
		}

		public SafeSaveValidationReport RestoreBackup(string destination, string backup) {
			var report = new SafeSaveValidationReport();
			string fullDestination = Path.GetFullPath(destination);
			string fullBackup = Path.GetFullPath(backup);
			string restorePoint = fullDestination + ".restore-point";
			string backupCopy = fullDestination + ".restore-safe-save-" + Guid.NewGuid().ToString("N") + ".tmp";

			try {
				if (!File.Exists(fullDestination)) {
					report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "recovery.destination-missing", fullDestination, "The destination GRF does not exist.");
					return report;
				}
				if (!File.Exists(fullBackup)) {
					report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "recovery.backup-missing", fullBackup, "The backup GRF does not exist.");
					return report;
				}
				if (File.Exists(restorePoint)) {
					report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "recovery.restore-point-exists", restorePoint, "An earlier restore point must be resolved before restoring another backup.");
					return report;
				}
				SafeSaveDestinationStamp destinationStamp = SafeSaveDestinationStamp.CaptureGrf(fullDestination);
				if (!destinationStamp.IsEditable) {
					report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "recovery.destination-not-editable", fullDestination, destinationStamp.ReasonCode);
					return report;
				}

				File.Copy(fullBackup, backupCopy, false);
				report = _validator(backupCopy, null) ?? new SafeSaveValidationReport();
				if (report.HasErrors) return report;
				SafeSaveDestinationStamp currentStamp = SafeSaveDestinationStamp.CaptureGrf(fullDestination);
				if (!currentStamp.IsEditable || !destinationStamp.Matches(currentStamp)) {
					report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "recovery.destination-changed", fullDestination, "The destination changed while the backup was being validated.");
					return report;
				}

				File.Replace(backupCopy, fullDestination, restorePoint, true);
				SafeSaveDestinationStamp replacedStamp = SafeSaveDestinationStamp.CaptureGrf(restorePoint);
				if (!replacedStamp.IsEditable || !destinationStamp.Matches(replacedStamp)) {
					string recoveryPath = fullDestination + ".restore-recovery-safe-save-" + Guid.NewGuid().ToString("N") + ".grf";
					try {
						File.Replace(restorePoint, fullDestination, recoveryPath, true);
					}
					catch (Exception rollbackException) {
						report.Add(SafeSavePhase.Promote, SafeSaveSeverity.Error, "recovery.rollback-failed", fullDestination, rollbackException.Message);
						return report;
					}
					report.Add(SafeSavePhase.Promote, SafeSaveSeverity.Error, "recovery.destination-changed", fullDestination, "The replaced file did not match the destination approved for restoration.");
					return report;
				}
				report = _validator(fullDestination, null) ?? new SafeSaveValidationReport();
				if (!report.HasErrors) File.Delete(restorePoint);
				return report;
			}
			catch (Exception exception) {
				report.Add(SafeSavePhase.Promote, SafeSaveSeverity.Error, "recovery.exception", fullDestination, exception.Message);
				return report;
			}
			finally {
				if (File.Exists(backupCopy)) {
					try { File.Delete(backupCopy); }
					catch { }
				}
			}
		}
	}
}
