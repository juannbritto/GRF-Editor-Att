using System;
using System.IO;

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
		private readonly ISafeSaveFileSystem _fileSystem;

		public SafeSaveCoordinator() : this(new SafeSaveFileSystem()) {
		}

		internal SafeSaveCoordinator(ISafeSaveFileSystem fileSystem) {
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		}

		public SafeSaveOutcome Execute(SafeSaveRequest request) {
			if (request == null) throw new ArgumentNullException(nameof(request));

			var report = new SafeSaveValidationReport();
			var options = request.Options ?? new SafeSaveOptions();
			string temporaryPath = request.DestinationPath + ".safe-save-" + Guid.NewGuid().ToString("N") + ".tmp";
			var outcome = new SafeSaveOutcome {
				TemporaryPath = temporaryPath,
				Report = report
			};
			SafeSavePhase currentPhase = SafeSavePhase.Preflight;

			try {
				EnterPhase(SafeSavePhase.Preflight, request.DestinationPath, options, report, ref currentPhase);
				string directory = Path.GetDirectoryName(Path.GetFullPath(request.DestinationPath));
				long requiredSpace = Math.Max(request.EstimatedLength, MinimumFreeSpace);
				if (_fileSystem.AvailableFreeSpace(directory) < requiredSpace) {
					report.Add(currentPhase, SafeSaveSeverity.Error, "space.insufficient", request.DestinationPath,
						"There is not enough free space to write the temporary file.");
					return outcome;
				}

				EnterPhase(SafeSavePhase.WriteTemporary, temporaryPath, options, report, ref currentPhase);
				if (request.WriteTemporary == null) throw new InvalidOperationException("A temporary writer is required.");
				request.WriteTemporary(temporaryPath);

				EnterPhase(SafeSavePhase.Validate, temporaryPath, options, report, ref currentPhase);
				if (request.ValidateTemporary == null) throw new InvalidOperationException("A temporary validator is required.");
				SafeSaveValidationReport validationReport = request.ValidateTemporary(temporaryPath);
				if (validationReport != null) report.Items.AddRange(validationReport.Items);
				if (report.HasErrors) return outcome;

				bool destinationExists = _fileSystem.Exists(request.DestinationPath);
				string backupPath = destinationExists && options.CreateBackup
					? request.DestinationPath + options.BackupSuffix
					: null;
				outcome.BackupPath = backupPath;

				EnterPhase(SafeSavePhase.Backup, backupPath ?? request.DestinationPath, options, report, ref currentPhase);
				EnterPhase(SafeSavePhase.Promote, request.DestinationPath, options, report, ref currentPhase);
				if (destinationExists) {
					_fileSystem.ReplaceExisting(temporaryPath, request.DestinationPath, backupPath);
				}
				else {
					_fileSystem.MoveNew(temporaryPath, request.DestinationPath);
				}

				EnterPhase(SafeSavePhase.Confirm, request.DestinationPath, options, report, ref currentPhase);
				outcome.Success = true;
			}
			catch (Exception exception) {
				outcome.Error = exception;
				report.Add(currentPhase, SafeSaveSeverity.Error, "safe-save.exception", request.DestinationPath, exception.Message);
			}
			finally {
				try {
					if (_fileSystem.Exists(temporaryPath)) _fileSystem.DeleteOwnedTemporary(temporaryPath);
				}
				catch (Exception exception) {
					report.Add(currentPhase, SafeSaveSeverity.Warning, "temporary.cleanup", temporaryPath, exception.Message);
				}
			}

			return outcome;
		}

		private static void EnterPhase(SafeSavePhase phase, string path, SafeSaveOptions options,
			SafeSaveValidationReport report, ref SafeSavePhase currentPhase) {
			currentPhase = phase;
			options.PhaseChanged?.Invoke(phase);
			if (options.IncludeInformationItems) {
				report.Add(phase, SafeSaveSeverity.Information, "safe-save.phase", path, "Safe-save phase started.");
			}
		}
	}
}
