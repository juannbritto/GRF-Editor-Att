using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace GRF.Core.SafeSave {
	internal sealed class SafeSaveRequest {
		public string DestinationPath { get; set; }
		public SafeSaveOptions Options { get; set; }
		public long EstimatedLength { get; set; }
		public Action<string> WriteTemporary { get; set; }
		public Func<string, SafeSaveValidationReport> ValidateTemporary { get; set; }
	}

	internal sealed class SafeSaveOutcome {
		public bool Success { get; internal set; }
		public string TemporaryPath { get; internal set; }
		public string BackupPath { get; internal set; }
		public SafeSaveValidationReport Report { get; internal set; }
		public Exception Error { get; internal set; }
	}

	internal sealed class SafeSaveCoordinator {
		private const long MinimumFreeSpace = 64 * 1024;
		private static readonly ISafeSavePathIdentityResolver DefaultPathIdentityResolver = new SafeSavePathIdentityResolver();
		private static readonly SafeSavePathLockManager DefaultPathLocks = new SafeSavePathLockManager(DefaultPathIdentityResolver);
		private static readonly AsyncLocal<ReservedPathContext> TransactionReservations =
			new AsyncLocal<ReservedPathContext>();
		private readonly ISafeSaveFileSystem _fileSystem;
		private readonly ISafeSavePathIdentityResolver _pathIdentityResolver;
		private readonly SafeSavePathLockManager _pathLocks;

		public SafeSaveCoordinator() : this(new SafeSaveFileSystem()) {
		}

		internal SafeSaveCoordinator(ISafeSaveFileSystem fileSystem)
			: this(fileSystem, DefaultPathIdentityResolver, DefaultPathLocks) {
		}

		internal SafeSaveCoordinator(ISafeSaveFileSystem fileSystem, ISafeSavePathIdentityResolver pathIdentityResolver,
			SafeSavePathLockManager pathLocks) {
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_pathIdentityResolver = pathIdentityResolver ?? throw new ArgumentNullException(nameof(pathIdentityResolver));
			_pathLocks = pathLocks ?? throw new ArgumentNullException(nameof(pathLocks));
		}

		public SafeSaveOutcome Execute(SafeSaveRequest request) {
			if (request == null) throw new ArgumentNullException(nameof(request));
			string destinationPath = Path.GetFullPath(request.DestinationPath);
			OptionsSnapshot options = OptionsSnapshot.Capture(request.Options ?? new SafeSaveOptions());
			string backupPath = Path.GetFullPath(destinationPath + options.BackupSuffix);
			if (string.Equals(destinationPath, backupPath, StringComparison.OrdinalIgnoreCase)) {
				throw new ArgumentException("Backup suffix must resolve to a path distinct from the destination.", nameof(request.Options.BackupSuffix));
			}
			string temporaryPath = destinationPath + ".safe-save-" + Guid.NewGuid().ToString("N") + ".tmp";
			string[] reservedPaths = { destinationPath, backupPath, temporaryPath };
			string[] reservedIdentities = reservedPaths.Select(_pathIdentityResolver.Resolve).ToArray();
			ReservedPathContext inheritedReservations = TransactionReservations.Value;
			if (inheritedReservations != null && inheritedReservations.Intersects(reservedIdentities)) {
				return ReentrantOutcome(destinationPath);
			}

			using (_pathLocks.AcquireIdentities(reservedIdentities)) {
				ReservedPathContext previous = TransactionReservations.Value;
				var current = new ReservedPathContext(previous, reservedIdentities);
				TransactionReservations.Value = current;
				try {
					return ExecuteLocked(request, destinationPath, backupPath, temporaryPath, reservedPaths, options);
				}
				finally {
					current.Deactivate();
					TransactionReservations.Value = previous;
				}
			}
		}

		private SafeSaveOutcome ExecuteLocked(SafeSaveRequest request, string destinationPath, string canonicalBackupPath,
			string temporaryPath, string[] reservedPaths, OptionsSnapshot options) {
			var report = new SafeSaveValidationReport();
			var outcome = new SafeSaveOutcome {
				TemporaryPath = temporaryPath,
				Report = report
			};
			SafeSavePhase currentPhase = SafeSavePhase.Preflight;
			bool canDeleteTemporary = true;

			try {
				NotifyPhase(SafeSavePhase.Preflight, destinationPath, reservedPaths, options, report, ref currentPhase);
				if (request.EstimatedLength < 0) {
					report.Add(currentPhase, SafeSaveSeverity.Error, "estimated-length.invalid", destinationPath,
						"Estimated length cannot be negative.");
					return outcome;
				}

				string directory = Path.GetDirectoryName(destinationPath);
				bool destinationExists = _fileSystem.Exists(destinationPath);
				long existingLength = destinationExists ? _fileSystem.Length(destinationPath) : 0;
				long requiredSpace = Math.Max(Math.Max(request.EstimatedLength, existingLength), MinimumFreeSpace);
				if (_fileSystem.AvailableFreeSpace(directory) < requiredSpace) {
					report.Add(currentPhase, SafeSaveSeverity.Error, "space.insufficient", destinationPath,
						"There is not enough free space to write the temporary file.");
					return outcome;
				}

				NotifyPhase(SafeSavePhase.WriteTemporary, temporaryPath, reservedPaths, options, report, ref currentPhase);
				if (request.WriteTemporary == null) throw new InvalidOperationException("A temporary writer is required.");
				request.WriteTemporary(temporaryPath);

				NotifyPhase(SafeSavePhase.Validate, temporaryPath, reservedPaths, options, report, ref currentPhase);
				if (request.ValidateTemporary == null) throw new InvalidOperationException("A temporary validator is required.");
				SafeSaveValidationReport validationReport = request.ValidateTemporary(temporaryPath);
				if (validationReport != null) report.Items.AddRange(validationReport.Items);
				if (report.HasErrors) return outcome;

				string backupPath = destinationExists && options.CreateBackup
					? canonicalBackupPath
					: null;
				outcome.BackupPath = backupPath;

				NotifyPhase(SafeSavePhase.Backup, backupPath ?? destinationPath, reservedPaths, options, report, ref currentPhase);
				NotifyPhase(SafeSavePhase.Promote, destinationPath, reservedPaths, options, report, ref currentPhase);
				if (destinationExists) {
					string retainedPreviousBackup = _fileSystem.ReplaceExisting(temporaryPath, destinationPath, backupPath);
					if (retainedPreviousBackup != null) {
						report.Add(SafeSavePhase.Promote, SafeSaveSeverity.Warning, "backup.previous-retained",
							retainedPreviousBackup, "The previous backup could not be removed and was retained for recovery.");
					}
				}
				else {
					_fileSystem.MoveNew(temporaryPath, destinationPath);
				}

				outcome.Success = true;
				NotifyPhase(SafeSavePhase.Confirm, destinationPath, reservedPaths, options, report, ref currentPhase);
			}
			catch (SafeSavePromotionException exception) {
				canDeleteTemporary = exception.CanDeleteTemporary;
				outcome.BackupPath = exception.ActualBackupPath;
				outcome.Error = exception;
				report.Add(currentPhase, SafeSaveSeverity.Error, exception.RecoveryCode,
					exception.RecoveryPath ?? destinationPath, exception.Message);
				if (exception.RetainedPreviousBackupPath != null) {
					report.Add(currentPhase, SafeSaveSeverity.Warning, "backup.previous-retained",
						exception.RetainedPreviousBackupPath,
						"The previous backup was retained under a unique recovery name.");
				}
			}
			catch (Exception exception) {
				if (currentPhase == SafeSavePhase.Promote && _fileSystem.Exists(temporaryPath)) {
					canDeleteTemporary = false;
					report.Add(currentPhase, SafeSaveSeverity.Warning, "temporary.retained-recovery", temporaryPath,
						"Promotion was ambiguous, so the validated temporary was retained for recovery.");
				}
				outcome.Error = exception;
				report.Add(currentPhase, SafeSaveSeverity.Error, "safe-save.exception", destinationPath, exception.Message);
			}
			finally {
				try {
					if (canDeleteTemporary && _fileSystem.Exists(temporaryPath)) _fileSystem.DeleteOwnedTemporary(temporaryPath);
				}
				catch (Exception exception) {
					report.Add(currentPhase, SafeSaveSeverity.Warning, "temporary.cleanup", temporaryPath, exception.Message);
				}
			}

			return outcome;
		}

		private static void NotifyPhase(SafeSavePhase phase, string path, string[] reservedPaths, OptionsSnapshot options,
			SafeSaveValidationReport report, ref SafeSavePhase currentPhase) {
			currentPhase = phase;
			Action<SafeSavePhase> callback = options.PhaseChanged;
			if (callback != null) {
				try {
					callback(phase);
				}
				catch (Exception exception) {
					report.Add(phase, SafeSaveSeverity.Warning, "progress.callback", path, exception.Message);
				}
			}
			if (options.IncludeInformationItems) {
				report.Add(phase, SafeSaveSeverity.Information, "safe-save.phase", path, "Safe-save phase started.");
			}
		}

		private static SafeSaveOutcome ReentrantOutcome(string destinationPath) {
			var report = new SafeSaveValidationReport();
			report.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error, "transaction.reentrant", destinationPath,
				"A safe-save callback cannot start a transaction that overlaps its reserved paths.");
			return new SafeSaveOutcome { Report = report };
		}

		private sealed class OptionsSnapshot {
			private OptionsSnapshot(bool createBackup, bool includeInformationItems, string backupSuffix,
				Action<SafeSavePhase> phaseChanged) {
				CreateBackup = createBackup;
				IncludeInformationItems = includeInformationItems;
				BackupSuffix = backupSuffix;
				PhaseChanged = phaseChanged;
			}

			internal bool CreateBackup { get; }
			internal bool IncludeInformationItems { get; }
			internal string BackupSuffix { get; }
			internal Action<SafeSavePhase> PhaseChanged { get; }

			internal static OptionsSnapshot Capture(SafeSaveOptions options) {
				string suffix = options.BackupSuffix;
				if (string.IsNullOrEmpty(suffix) ||
					suffix != suffix.TrimEnd(' ', '.') ||
					suffix.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
					suffix.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
					suffix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
					throw new ArgumentException("Backup suffix must be non-empty and cannot contain path separators or invalid file-name characters.", nameof(options.BackupSuffix));
				}

				return new OptionsSnapshot(options.CreateBackup, options.IncludeInformationItems, suffix, options.PhaseChanged);
			}
		}

		private sealed class ReservedPathContext {
			private readonly HashSet<string> _paths;
			private readonly ReservedPathContext _parent;
			private int _active = 1;

			internal ReservedPathContext(ReservedPathContext parent, IEnumerable<string> paths) {
				_parent = parent;
				_paths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
			}

			internal bool Intersects(IEnumerable<string> paths) {
				for (ReservedPathContext context = this; context != null; context = context._parent) {
					if (Volatile.Read(ref context._active) == 0) continue;
					foreach (string path in paths) {
						if (context._paths.Contains(path)) return true;
					}
				}
				return false;
			}

			internal void Deactivate() {
				Interlocked.Exchange(ref _active, 0);
			}
		}
	}
}
