using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class SafeSaveCoordinatorTests {
		[TestMethod]
		public void Validation_failure_never_replaces_destination() {
			var fileSystem = ExistingDestinationFileSystem();
			var coordinator = new SafeSaveCoordinator(fileSystem);

			SafeSaveOutcome outcome = coordinator.Execute(Request(fileSystem, report =>
				report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "invalid", "archive.grf", "Invalid temporary file.")));

			Assert.IsFalse(outcome.Success);
			Assert.AreEqual(0, fileSystem.ReplaceCount);
			Assert.AreEqual(0, fileSystem.MoveCount);
			Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
			Assert.IsTrue(outcome.Report.HasErrors);
		}

		[TestMethod]
		public void Existing_destination_is_atomically_replaced_and_backed_up() {
			var fileSystem = ExistingDestinationFileSystem();

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.AreEqual(1, fileSystem.ReplaceCount);
			Assert.AreEqual(0, fileSystem.MoveCount);
			Assert.AreEqual("archive.grf.bak", fileSystem.LastBackupPath);
			Assert.AreEqual("archive.grf.bak", outcome.BackupPath);
		}

		[TestMethod]
		public void Promotion_failure_keeps_destination_and_reports_error() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ThrowOnReplace = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains("archive.grf"));
			Assert.AreEqual(0, fileSystem.MoveCount);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Phase == SafeSavePhase.Promote && item.Severity == SafeSaveSeverity.Error));
			Assert.IsNotNull(outcome.Error);
		}

		[TestMethod]
		public void Cancelled_or_throwing_write_removes_only_owned_temporary_file() {
			var fileSystem = ExistingDestinationFileSystem();
			var request = Request(fileSystem);
			request.WriteTemporary = path => {
				fileSystem.Files.Add(path);
				throw new OperationCanceledException("cancelled");
			};

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
			Assert.IsTrue(fileSystem.Files.Contains("archive.grf"));
			Assert.IsFalse(fileSystem.DeletedPaths.Contains("archive.grf"));
			Assert.IsFalse(fileSystem.DeletedPaths.Contains("archive.grf.bak"));
		}

		[TestMethod]
		public void Insufficient_space_stops_before_writer() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.FreeSpace = 65535;
			bool writerCalled = false;
			var request = Request(fileSystem);
			request.EstimatedLength = 1;
			request.WriteTemporary = path => writerCalled = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(writerCalled);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Phase == SafeSavePhase.Preflight && item.Severity == SafeSaveSeverity.Error));
		}

		[TestMethod]
		public void New_destination_uses_move_not_replace() {
			var fileSystem = new RecordingFileSystem();

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.AreEqual(1, fileSystem.MoveCount);
			Assert.AreEqual(0, fileSystem.ReplaceCount);
			Assert.IsNull(outcome.BackupPath);
		}

		[TestMethod]
		public void PhaseChanged_has_exact_order_and_information_verbosity() {
			var fileSystem = ExistingDestinationFileSystem();
			var phases = new List<SafeSavePhase>();
			var request = Request(fileSystem);
			request.Options.PhaseChanged = phases.Add;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			CollectionAssert.AreEqual(new[] {
				SafeSavePhase.Preflight,
				SafeSavePhase.WriteTemporary,
				SafeSavePhase.Validate,
				SafeSavePhase.Backup,
				SafeSavePhase.Promote,
				SafeSavePhase.Confirm
			}, phases);
			CollectionAssert.AreEqual(phases, outcome.Report.Items
				.Where(item => item.Severity == SafeSaveSeverity.Information)
				.Select(item => item.Phase).ToList());

			request.Options.IncludeInformationItems = false;
			SafeSaveOutcome quietOutcome = new SafeSaveCoordinator(ExistingDestinationFileSystem()).Execute(request);
			Assert.AreEqual(0, quietOutcome.Report.Items.Count(item => item.Severity == SafeSaveSeverity.Information));
		}

		private static RecordingFileSystem ExistingDestinationFileSystem() {
			var fileSystem = new RecordingFileSystem();
			fileSystem.Files.Add("archive.grf");
			return fileSystem;
		}

		private static SafeSaveRequest Request(RecordingFileSystem fileSystem, Action<SafeSaveValidationReport> validation = null) {
			return new SafeSaveRequest {
				DestinationPath = "archive.grf",
				Options = new SafeSaveOptions(),
				EstimatedLength = 1024,
				WriteTemporary = path => fileSystem.Files.Add(path),
				ValidateTemporary = path => {
					var report = new SafeSaveValidationReport();
					validation?.Invoke(report);
					return report;
				}
			};
		}

		private sealed class RecordingFileSystem : ISafeSaveFileSystem {
			public readonly HashSet<string> Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			public readonly List<string> DeletedPaths = new List<string>();
			public long FreeSpace = long.MaxValue;
			public bool ThrowOnReplace;
			public int DeleteTemporaryCount;
			public int MoveCount;
			public int ReplaceCount;
			public string LastBackupPath;

			public bool Exists(string path) => Files.Contains(path);
			public long Length(string path) => 1;
			public long AvailableFreeSpace(string directory) => FreeSpace;

			public void DeleteOwnedTemporary(string path) {
				DeleteTemporaryCount++;
				DeletedPaths.Add(path);
				Files.Remove(path);
			}

			public void MoveNew(string temporaryPath, string destinationPath) {
				MoveCount++;
				Files.Remove(temporaryPath);
				Files.Add(destinationPath);
			}

			public void ReplaceExisting(string temporaryPath, string destinationPath, string backupPath) {
				ReplaceCount++;
				LastBackupPath = backupPath;
				if (ThrowOnReplace) throw new IOException("promotion failed");
				Files.Remove(temporaryPath);
				Files.Add(destinationPath);
				if (backupPath != null) Files.Add(backupPath);
			}
		}
	}

	[TestClass]
	public class SafeSaveFileSystemTests {
		private string _temporaryDirectory;

		[TestInitialize]
		public void SetUp() {
			_temporaryDirectory = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_temporaryDirectory);
		}

		[TestCleanup]
		public void TearDown() {
			if (Directory.Exists(_temporaryDirectory)) Directory.Delete(_temporaryDirectory, true);
		}

		[TestMethod]
		public void DeleteOwnedTemporary_rejects_non_owned_name() {
			string path = Path.Combine(_temporaryDirectory, "ordinary.tmp");
			File.WriteAllText(path, "keep");

			bool rejected = false;
			try {
				new SafeSaveFileSystem().DeleteOwnedTemporary(path);
			}
			catch (InvalidOperationException) {
				rejected = true;
			}

			Assert.IsTrue(rejected);
			Assert.IsTrue(File.Exists(path));
		}

		[TestMethod]
		public void ReplaceExisting_promotes_temp_and_backs_up_original() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");

			new SafeSaveFileSystem().ReplaceExisting(temporary, destination, backup);

			Assert.AreEqual("replacement", File.ReadAllText(destination));
			Assert.AreEqual("original", File.ReadAllText(backup));
			Assert.IsFalse(File.Exists(temporary));
		}
	}
}
