using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
			string destination = Path.GetFullPath("archive.grf");

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.AreEqual(1, fileSystem.ReplaceCount);
			Assert.AreEqual(0, fileSystem.MoveCount);
			Assert.AreEqual(destination + ".bak", fileSystem.LastBackupPath);
			Assert.AreEqual(destination + ".bak", outcome.BackupPath);
		}

		[TestMethod]
		public void Promotion_failure_keeps_destination_and_reports_error() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ThrowOnReplace = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
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
			string destination = Path.GetFullPath("archive.grf");
			Assert.IsTrue(fileSystem.Files.Contains(destination));
			Assert.IsFalse(fileSystem.DeletedPaths.Contains(destination));
			Assert.IsFalse(fileSystem.DeletedPaths.Contains(destination + ".bak"));
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
		public void Destination_preflight_runs_under_transaction_before_temporary_writer() {
			var fileSystem = ExistingDestinationFileSystem();
			var coordinator = new SafeSaveCoordinator(fileSystem);
			var request = Request(fileSystem);
			bool writerCalled = false;
			bool nestedWriterCalled = false;
			request.WriteTemporary = path => writerCalled = true;
			request.ValidateDestination = (destination, destinationExists) => {
				Assert.IsTrue(destinationExists);
				var nested = Request(fileSystem);
				nested.WriteTemporary = path => nestedWriterCalled = true;
				SafeSaveOutcome nestedOutcome = coordinator.Execute(nested);
				Assert.IsFalse(nestedOutcome.Success);
				Assert.IsTrue(nestedOutcome.Report.Items.Any(item => item.Code == "transaction.reentrant"));
				throw new InvalidOperationException("blocked destination");
			};

			SafeSaveOutcome outcome = coordinator.Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(writerCalled);
			Assert.IsFalse(nestedWriterCalled);
			Assert.AreEqual(0, fileSystem.ReplaceCount);
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
		}

		[TestMethod]
		public void Destination_is_revalidated_after_temporary_validation_immediately_before_promotion() {
			var fileSystem = ExistingDestinationFileSystem();
			var request = Request(fileSystem);
			bool temporaryValidated = false;
			bool promoteValidationCalled = false;
			request.ValidateTemporary = path => {
				temporaryValidated = true;
				return new SafeSaveValidationReport();
			};
			request.ValidateDestinationBeforePromote = (destination, destinationExists) => {
				Assert.IsTrue(temporaryValidated);
				Assert.IsTrue(destinationExists);
				promoteValidationCalled = true;
				throw new InvalidOperationException("destination changed");
			};

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(promoteValidationCalled);
			Assert.AreEqual(0, fileSystem.ReplaceCount);
			Assert.AreEqual(0, fileSystem.MoveCount);
			Assert.IsNull(outcome.BackupPath);
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
		public void Ambiguous_move_failure_with_destination_created_preserves_validated_temporary() {
			var fileSystem = new RecordingFileSystem { ThrowOnMoveAndCreateDestination = true };

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
			Assert.IsTrue(fileSystem.Files.Any(path => path.Contains(".safe-save-")));
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
		}

		[TestMethod]
		public void Ambiguous_move_failure_without_destination_preserves_validated_temporary() {
			var fileSystem = new RecordingFileSystem { ThrowOnMove = true };

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
			Assert.IsTrue(fileSystem.Files.Any(path => path.Contains(".safe-save-")));
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
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

		[TestMethod]
		public void Relative_destination_is_canonicalized_before_callbacks_change_current_directory() {
			string originalCurrentDirectory = Environment.CurrentDirectory;
			string startingDirectory = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"));
			string changedDirectory = Path.Combine(startingDirectory, "changed");
			Directory.CreateDirectory(changedDirectory);

			try {
				Environment.CurrentDirectory = startingDirectory;
				var promoteFileSystem = new RecordingFileSystem();
				var promoteRequest = Request(promoteFileSystem);
				string writerPath = null;
				string validatorPath = null;
				promoteRequest.WriteTemporary = path => {
					writerPath = path;
					promoteFileSystem.Files.Add(path);
					Environment.CurrentDirectory = changedDirectory;
				};
				promoteRequest.ValidateTemporary = path => {
					validatorPath = path;
					return new SafeSaveValidationReport();
				};

				SafeSaveOutcome promoted = new SafeSaveCoordinator(promoteFileSystem).Execute(promoteRequest);
				string expectedDestination = Path.Combine(startingDirectory, "archive.grf");
				Assert.IsTrue(promoted.Success, promoted.Report.ToString());
				Assert.AreEqual(expectedDestination, promoteFileSystem.LastDestinationPath);
				Assert.IsTrue(writerPath.StartsWith(expectedDestination + ".safe-save-", StringComparison.OrdinalIgnoreCase));
				Assert.AreEqual(writerPath, validatorPath);

				Environment.CurrentDirectory = startingDirectory;
				var cleanupFileSystem = new RecordingFileSystem();
				var cleanupRequest = Request(cleanupFileSystem);
				cleanupRequest.WriteTemporary = path => {
					cleanupFileSystem.Files.Add(path);
					Environment.CurrentDirectory = changedDirectory;
					throw new IOException("write failed");
				};

				SafeSaveOutcome failed = new SafeSaveCoordinator(cleanupFileSystem).Execute(cleanupRequest);
				Assert.IsFalse(failed.Success);
				Assert.IsTrue(cleanupFileSystem.DeletedPaths.Single().StartsWith(expectedDestination + ".safe-save-", StringComparison.OrdinalIgnoreCase));
			}
			finally {
				Environment.CurrentDirectory = originalCurrentDirectory;
				if (Directory.Exists(startingDirectory)) Directory.Delete(startingDirectory, true);
			}
		}

		[TestMethod]
		public void Throwing_confirm_callback_cannot_invalidate_successful_promotion() {
			var fileSystem = new RecordingFileSystem();
			var request = Request(fileSystem);
			request.Options.PhaseChanged = phase => {
				if (phase == SafeSavePhase.Confirm) throw new InvalidOperationException("progress listener failed");
			};

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.AreEqual(1, fileSystem.MoveCount);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Phase == SafeSavePhase.Confirm &&
				item.Severity == SafeSaveSeverity.Warning && item.Code == "progress.callback"));
		}

		[TestMethod]
		public void Negative_estimated_length_is_rejected_before_writer() {
			var fileSystem = new RecordingFileSystem();
			bool writerCalled = false;
			var request = Request(fileSystem);
			request.EstimatedLength = -1;
			request.WriteTemporary = path => writerCalled = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(writerCalled);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Phase == SafeSavePhase.Preflight &&
				item.Severity == SafeSaveSeverity.Error));
		}

		[TestMethod]
		public void Existing_destination_length_prevents_underestimated_write() {
			var fileSystem = new RecordingFileSystem {
				FreeSpace = 70000,
				LengthValue = 100000
			};
			fileSystem.Files.Add(Path.GetFullPath("archive.grf"));
			bool writerCalled = false;
			var request = Request(fileSystem);
			request.EstimatedLength = 1;
			request.WriteTemporary = path => writerCalled = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(writerCalled);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "space.insufficient"));
		}

		[TestMethod]
		public void Same_destination_serializes_entire_transaction() {
			string destination = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"), "archive.grf");
			var firstFileSystem = new RecordingFileSystem();
			var secondFileSystem = new RecordingFileSystem();
			var firstRequest = Request(firstFileSystem);
			var secondRequest = Request(secondFileSystem);
			firstRequest.DestinationPath = destination;
			secondRequest.DestinationPath = destination;
			var startGate = new ManualResetEventSlim(false);
			var maximumSync = new object();
			int tasksStarted = 0;
			int activeWriters = 0;
			int maximumWriters = 0;

			Action<RecordingFileSystem, SafeSaveRequest> configureWriter = (fileSystem, request) => {
				request.WriteTemporary = path => {
					startGate.Wait();
					int active = Interlocked.Increment(ref activeWriters);
					lock (maximumSync) maximumWriters = Math.Max(maximumWriters, active);
					Thread.Sleep(150);
					fileSystem.Files.Add(path);
					Interlocked.Decrement(ref activeWriters);
				};
			};
			configureWriter(firstFileSystem, firstRequest);
			configureWriter(secondFileSystem, secondRequest);

			Task<SafeSaveOutcome> first = Task.Run(() => {
				Interlocked.Increment(ref tasksStarted);
				return new SafeSaveCoordinator(firstFileSystem).Execute(firstRequest);
			});
			Task<SafeSaveOutcome> second = Task.Run(() => {
				Interlocked.Increment(ref tasksStarted);
				return new SafeSaveCoordinator(secondFileSystem).Execute(secondRequest);
			});
			Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref tasksStarted) == 2, 5000));
			startGate.Set();

			Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, 10000), "Concurrent saves did not finish.");
			Assert.AreEqual(1, maximumWriters);
			Assert.IsTrue(first.Result.Success, first.Result.Report.ToString());
			Assert.IsTrue(second.Result.Success, second.Result.Report.ToString());
		}

		[TestMethod]
		public void Path_lock_entries_are_removed_after_unique_destinations_are_released() {
			var manager = new SafeSavePathLockManager();

			for (int index = 0; index < 500; index++) {
				string destination = Path.GetFullPath("archive-" + index + ".grf");
				using (manager.Acquire(new[] { destination, destination.ToUpperInvariant() })) {
					Assert.AreEqual(1, manager.ActiveEntryCount);
				}
			}

			Assert.AreEqual(0, manager.ActiveEntryCount);
		}

		[TestMethod]
		public void Independent_lock_managers_serialize_through_named_mutex() {
			string path = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"), "archive.grf");
			var firstManager = new SafeSavePathLockManager();
			var secondManager = new SafeSavePathLockManager();
			var gate = new ManualResetEventSlim(false);
			int active = 0;
			int maximum = 0;

			Func<SafeSavePathLockManager, Task> run = manager => Task.Run(() => {
				gate.Wait();
				using (manager.Acquire(new[] { path })) {
					int now = Interlocked.Increment(ref active);
					UpdateMaximum(ref maximum, now);
					Thread.Sleep(100);
					Interlocked.Decrement(ref active);
				}
			});
			Task first = run(firstManager);
			Task second = run(secondManager);
			gate.Set();

			Assert.IsTrue(Task.WaitAll(new[] { first, second }, 10000), "Named mutex leases did not finish.");
			Assert.AreEqual(1, maximum);
		}

		[TestMethod]
		public void Named_mutex_uses_machine_wide_global_namespace() {
			string name = SafeSavePathLockManager.GetMutexName(Path.GetFullPath("archive.grf"));

			StringAssert.StartsWith(name, @"Global\GRFEditor.SafeSave.");
		}

		[TestMethod]
		public void Coordinator_resolves_each_reserved_path_once_and_passes_identity_snapshot_to_lock_manager() {
			var resolver = new MutableIdentityResolver();
			var manager = new SafeSavePathLockManager(resolver);
			var fileSystem = new RecordingFileSystem();
			var coordinator = new SafeSaveCoordinator(fileSystem, resolver, manager);

			SafeSaveOutcome outcome = coordinator.Execute(Request(fileSystem));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.AreEqual(3, resolver.TotalCalls, "Destination, backup and temporary must each be resolved exactly once.");
			Assert.AreEqual(3, resolver.CallsByPath.Count);
			Assert.IsTrue(resolver.CallsByPath.All(pair => pair.Value == 1));
		}

		[TestMethod]
		public void Windows_path_identity_resolves_directory_junction_alias_when_supported() {
			string root = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"));
			string real = Path.Combine(root, "real");
			string alias = Path.Combine(root, "alias");
			Directory.CreateDirectory(real);
			try {
				var startInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe",
					"/c mklink /J \"" + alias + "\" \"" + real + "\"") {
					CreateNoWindow = true,
					UseShellExecute = false
				};
				using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)) {
					process.WaitForExit();
					if (process.ExitCode != 0) Assert.Inconclusive("Junction creation is unavailable in this environment.");
				}

				var resolver = new SafeSavePathIdentityResolver();
				string realIdentity = resolver.Resolve(Path.Combine(real, "future.grf"));
				string aliasIdentity = resolver.Resolve(Path.Combine(alias, "future.grf"));
				Assert.AreEqual(realIdentity, aliasIdentity, true);
			}
			finally {
				if (Directory.Exists(alias)) Directory.Delete(alias);
				if (Directory.Exists(root)) Directory.Delete(root, true);
			}
		}

		[TestMethod]
		public void Destination_and_its_sidecar_are_serialized_even_when_backup_is_disabled() {
			string destination = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"), "x.grf");
			var firstFileSystem = new RecordingFileSystem();
			var secondFileSystem = new RecordingFileSystem();
			var firstRequest = Request(firstFileSystem);
			var secondRequest = Request(secondFileSystem);
			firstRequest.DestinationPath = destination;
			firstRequest.Options.CreateBackup = false;
			secondRequest.DestinationPath = destination + firstRequest.Options.BackupSuffix;
			var writerGate = new ManualResetEventSlim(false);
			int activeWriters = 0;
			int maximumWriters = 0;

			Action<RecordingFileSystem, SafeSaveRequest> configure = (fileSystem, request) => {
				request.WriteTemporary = path => {
					writerGate.Wait();
					int active = Interlocked.Increment(ref activeWriters);
					UpdateMaximum(ref maximumWriters, active);
					Thread.Sleep(100);
					fileSystem.Files.Add(path);
					Interlocked.Decrement(ref activeWriters);
				};
			};
			configure(firstFileSystem, firstRequest);
			configure(secondFileSystem, secondRequest);

			Task<SafeSaveOutcome> first = Task.Run(() => new SafeSaveCoordinator(firstFileSystem).Execute(firstRequest));
			Task<SafeSaveOutcome> second = Task.Run(() => new SafeSaveCoordinator(secondFileSystem).Execute(secondRequest));
			writerGate.Set();

			Assert.IsTrue(Task.WaitAll(new Task[] { first, second }, 10000), "Overlapping saves did not finish.");
			Assert.AreEqual(1, maximumWriters);
			Assert.IsTrue(first.Result.Success, first.Result.Report.ToString());
			Assert.IsTrue(second.Result.Success, second.Result.Report.ToString());
		}

		[TestMethod]
		public void Options_are_snapshotted_before_writer_mutates_backup_suffix() {
			string destination = Path.GetFullPath("snapshot.grf");
			var outerFileSystem = new RecordingFileSystem();
			outerFileSystem.Files.Add(destination);
			var outerRequest = Request(outerFileSystem);
			outerRequest.DestinationPath = destination;
			var originalCallbackPhases = new List<SafeSavePhase>();
			bool replacementCallbackCalled = false;
			outerRequest.Options.PhaseChanged = originalCallbackPhases.Add;
			SafeSaveOutcome sidecarOutcome = null;
			outerRequest.WriteTemporary = path => {
				outerRequest.Options.BackupSuffix = ".alt";
				outerRequest.Options.CreateBackup = false;
				outerRequest.Options.IncludeInformationItems = false;
				outerRequest.Options.PhaseChanged = phase => replacementCallbackCalled = true;
				var sidecarFileSystem = new RecordingFileSystem();
				var sidecarRequest = Request(sidecarFileSystem);
				sidecarRequest.DestinationPath = destination + ".alt";
				sidecarOutcome = new SafeSaveCoordinator(sidecarFileSystem).Execute(sidecarRequest);
				outerFileSystem.Files.Add(path);
			};

			SafeSaveOutcome outcome = new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest);

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.IsTrue(sidecarOutcome.Success, sidecarOutcome.Report.ToString());
			Assert.AreEqual(destination + ".bak", outcome.BackupPath);
			Assert.AreEqual(destination + ".bak", outerFileSystem.LastBackupPath);
			Assert.IsFalse(outerFileSystem.Files.Contains(destination + ".alt"));
			Assert.AreEqual(6, originalCallbackPhases.Count);
			Assert.IsFalse(replacementCallbackCalled);
			Assert.AreEqual(6, outcome.Report.Items.Count(item => item.Code == "safe-save.phase"));
		}

		[TestMethod]
		public void Invalid_backup_suffixes_are_rejected_before_writer() {
			foreach (string invalidSuffix in new[] { null, "", ".", " ", ". ", ".bak.", ".bak ", "bad/sidecar", "bad\\sidecar", "bad:sidecar" }) {
				var fileSystem = ExistingDestinationFileSystem();
				var request = Request(fileSystem);
				request.Options.BackupSuffix = invalidSuffix;
				bool writerCalled = false;
				request.WriteTemporary = path => writerCalled = true;
				bool rejected = false;
				try {
					new SafeSaveCoordinator(fileSystem).Execute(request);
				}
				catch (ArgumentException) {
					rejected = true;
				}
				Assert.IsTrue(rejected, "Suffix should have been rejected: " + invalidSuffix);
				Assert.IsFalse(writerCalled);
			}
		}

		[TestMethod]
		public void Replace_failure_with_only_temporary_left_restores_validated_replacement_without_reporting_success() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ReplaceFailure = ReplaceFailureState.DestinationMissingTemporaryOnly;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			string destination = Path.GetFullPath("archive.grf");
			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains(destination), "Validated replacement should be restored to destination.");
			Assert.IsFalse(fileSystem.Files.Any(path => path.Contains(".safe-save-")));
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount, "The recovery move, not finally cleanup, owns the only replacement.");
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "promotion.recovered-replacement"));
		}

		[TestMethod]
		public void Replace_failure_with_destination_missing_and_backup_restores_backup_and_retains_temporary() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ReplaceFailure = ReplaceFailureState.DestinationMissingBackupAndTemporary;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			string destination = Path.GetFullPath("archive.grf");
			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains(destination), "Backup should be restored to destination.");
			Assert.IsTrue(fileSystem.Files.Contains(destination + ".bak"), "Recovery backup must remain intact.");
			Assert.IsTrue(fileSystem.Files.Any(path => path.Contains(".safe-save-")), "Validated temporary must remain as a recovery artifact.");
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount, "The only replacement must not be destroyed.");
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "promotion.restored-backup"));
		}

		[TestMethod]
		public void Failed_backup_recreation_clears_outcome_backup_path_when_bak_is_unavailable() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ReplaceFailure = ReplaceFailureState.DestinationMissingBackupAndTemporary;
			fileSystem.BackupUnavailableOnFailure = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsNull(outcome.BackupPath, "Outcome must describe the actual backup state after reconciliation.");
		}

		[TestMethod]
		public void Replace_failure_with_destination_intact_cleans_residual_temporary() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ReplaceFailure = ReplaceFailureState.DestinationAndTemporary;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
			Assert.AreEqual(1, fileSystem.DeleteTemporaryCount);
			Assert.IsFalse(fileSystem.Files.Any(path => path.Contains(".safe-save-")));
		}

		[TestMethod]
		public void Unexpected_promotion_exception_never_deletes_temporary_when_destination_is_missing() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.ThrowOnReplaceAndRemoveDestination = true;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsFalse(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
			Assert.IsTrue(fileSystem.Files.Any(path => path.Contains(".safe-save-")), "Unknown replace failures must favor recovery over cleanup.");
			Assert.AreEqual(0, fileSystem.DeleteTemporaryCount);
		}

		[TestMethod]
		public void Nested_execute_from_writer_fails_fast_without_entering_nested_writer() {
			string destination = Path.GetFullPath("writer-reentrant.grf");
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			var nestedRequest = Request(nestedFileSystem);
			outerRequest.DestinationPath = destination;
			nestedRequest.DestinationPath = destination;
			bool nestedWriterCalled = false;
			nestedRequest.WriteTemporary = path => nestedWriterCalled = true;
			SafeSaveOutcome nestedOutcome = null;
			outerRequest.WriteTemporary = path => {
				nestedOutcome = new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest);
				outerFileSystem.Files.Add(path);
			};

			Task<SafeSaveOutcome> outer = Task.Run(() => new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest));

			Assert.IsTrue(outer.Wait(5000), "Nested writer save deadlocked.");
			Assert.IsTrue(outer.Result.Success, outer.Result.Report.ToString());
			Assert.IsFalse(nestedOutcome.Success);
			Assert.IsFalse(nestedWriterCalled);
			Assert.IsTrue(nestedOutcome.Report.Items.Any(item => item.Code == "transaction.reentrant"));
		}

		[TestMethod]
		public void Nested_execute_targeting_outer_temporary_fails_fast() {
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			SafeSaveOutcome nestedOutcome = null;
			bool nestedWriterCalled = false;
			outerRequest.WriteTemporary = temporaryPath => {
				var nestedRequest = Request(nestedFileSystem);
				nestedRequest.DestinationPath = temporaryPath;
				nestedRequest.WriteTemporary = path => nestedWriterCalled = true;
				nestedOutcome = new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest);
				outerFileSystem.Files.Add(temporaryPath);
			};

			Task<SafeSaveOutcome> outer = Task.Run(() => new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest));

			Assert.IsTrue(outer.Wait(5000), "Nested save targeting outer temporary deadlocked.");
			Assert.IsTrue(outer.Result.Success, outer.Result.Report.ToString());
			Assert.IsNotNull(nestedOutcome);
			Assert.IsFalse(nestedOutcome.Success);
			Assert.IsFalse(nestedWriterCalled);
			Assert.IsTrue(nestedOutcome.Report.Items.Any(item => item.Code == "transaction.reentrant"));
		}

		[TestMethod]
		public void Nested_task_from_validator_fails_fast_without_entering_nested_validator() {
			string destination = Path.GetFullPath("validator-reentrant.grf");
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			var nestedRequest = Request(nestedFileSystem);
			outerRequest.DestinationPath = destination;
			nestedRequest.DestinationPath = destination;
			bool nestedValidatorCalled = false;
			nestedRequest.ValidateTemporary = path => {
				nestedValidatorCalled = true;
				return new SafeSaveValidationReport();
			};
			SafeSaveOutcome nestedOutcome = null;
			outerRequest.ValidateTemporary = path => {
				Task<SafeSaveOutcome> nested = Task.Run(() => new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest));
				Assert.IsTrue(nested.Wait(5000), "Nested validator save deadlocked.");
				nestedOutcome = nested.Result;
				return new SafeSaveValidationReport();
			};

			SafeSaveOutcome outer = new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest);

			Assert.IsTrue(outer.Success, outer.Report.ToString());
			Assert.IsFalse(nestedOutcome.Success);
			Assert.IsFalse(nestedValidatorCalled);
			Assert.IsTrue(nestedOutcome.Report.Items.Any(item => item.Code == "transaction.reentrant"));
		}

		[TestMethod]
		public void Captured_transaction_context_expires_when_outer_execute_returns() {
			string destination = Path.GetFullPath("expired-context.grf");
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			var nestedRequest = Request(nestedFileSystem);
			outerRequest.DestinationPath = destination;
			nestedRequest.DestinationPath = destination;
			var releaseNested = new ManualResetEventSlim(false);
			Task<SafeSaveOutcome> nested = null;
			outerRequest.WriteTemporary = path => {
				nested = Task.Run(() => {
					releaseNested.Wait();
					return new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest);
				});
				outerFileSystem.Files.Add(path);
			};

			SafeSaveOutcome outer = new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest);
			releaseNested.Set();

			Assert.IsTrue(nested.Wait(5000), "Captured task did not finish after outer transaction.");
			Assert.IsTrue(outer.Success, outer.Report.ToString());
			Assert.IsTrue(nested.Result.Success, nested.Result.Report.ToString());
			Assert.IsFalse(nested.Result.Report.Items.Any(item => item.Code == "transaction.reentrant"));
		}

		[TestMethod]
		public void Reentrant_phase_callback_on_same_destination_fails_fast_without_deadlock() {
			string destination = Path.GetFullPath("reentrant.grf");
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			var nestedRequest = Request(nestedFileSystem);
			outerRequest.DestinationPath = destination;
			nestedRequest.DestinationPath = destination;
			SafeSaveOutcome nestedOutcome = null;
			int entered = 0;
			outerRequest.Options.PhaseChanged = phase => {
				if (Interlocked.Exchange(ref entered, 1) != 0) return;
				nestedOutcome = new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest);
			};

			Task<SafeSaveOutcome> outer = Task.Run(() => new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest));

			Assert.IsTrue(outer.Wait(5000), "Outer save deadlocked in its progress callback.");
			Assert.IsTrue(outer.Result.Success, outer.Result.Report.ToString());
			Assert.IsNotNull(nestedOutcome);
			Assert.IsFalse(nestedOutcome.Success);
			Assert.IsTrue(nestedOutcome.Report.Items.Any(item => item.Code == "transaction.reentrant"));
			Assert.AreEqual(0, nestedFileSystem.MoveCount + nestedFileSystem.ReplaceCount);
			Assert.IsFalse(outer.Result.Report.Items.Any(item => item.Code == "progress.callback-timeout"));
		}

		[TestMethod]
		public void Phase_callback_tasks_inherit_reservations_and_all_finish_before_execute_returns() {
			string destination = Path.GetFullPath("callback-task.grf");
			var outerFileSystem = new RecordingFileSystem();
			var nestedFileSystem = new RecordingFileSystem();
			var outerRequest = Request(outerFileSystem);
			var nestedRequest = Request(nestedFileSystem);
			outerRequest.DestinationPath = destination;
			nestedRequest.DestinationPath = destination;
			var nestedTasks = new List<Task<SafeSaveOutcome>>();
			int entered = 0;
			outerRequest.Options.PhaseChanged = phase => {
				if (Interlocked.Exchange(ref entered, 1) != 0) return;
				for (int index = 0; index < 5; index++) {
					Task<SafeSaveOutcome> nested = Task.Run(() => new SafeSaveCoordinator(nestedFileSystem).Execute(nestedRequest));
					nestedTasks.Add(nested);
					nested.Wait();
				}
			};

			Task<SafeSaveOutcome> outer = Task.Run(() => new SafeSaveCoordinator(outerFileSystem).Execute(outerRequest));

			Assert.IsTrue(outer.Wait(5000), "Outer save deadlocked while its callback waited for a nested save.");
			Assert.IsTrue(outer.Result.Success, outer.Result.Report.ToString());
			Assert.AreEqual(5, nestedTasks.Count);
			Assert.IsTrue(nestedTasks.All(task => task.IsCompleted));
			Assert.IsTrue(nestedTasks.All(task => !task.Result.Success &&
				task.Result.Report.Items.Any(item => item.Code == "transaction.reentrant")));
			Assert.IsFalse(outer.Result.Report.Items.Any(item => item.Code == "progress.callback-timeout"));
		}

		[TestMethod]
		public void Retained_previous_backup_is_reported_as_warning_without_failing_save() {
			var fileSystem = ExistingDestinationFileSystem();
			string retained = Path.GetFullPath("archive.grf.bak.previous-safe-save-retained");
			fileSystem.ResidualPreviousBackupPath = retained;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Severity == SafeSaveSeverity.Warning &&
				item.Code == "backup.previous-retained" && item.Path == retained));
		}

		[TestMethod]
		public void Retained_previous_backup_is_reported_when_promotion_fails() {
			var fileSystem = ExistingDestinationFileSystem();
			string retained = Path.GetFullPath("archive.grf.bak.previous-safe-save-retained-on-failure");
			fileSystem.ReplaceFailure = ReplaceFailureState.DestinationAndTemporary;
			fileSystem.RetainedPreviousBackupPathOnFailure = retained;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(fileSystem));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Severity == SafeSaveSeverity.Warning &&
				item.Code == "backup.previous-retained" && item.Path == retained));
		}

		[TestMethod]
		public void Concurrent_destination_swap_is_rolled_back_and_validated_output_is_retained() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.BackupMatchesCapturedStamp = false;
			SafeSaveRequest request = Request(fileSystem);
			request.CaptureDestinationStamp = path => "classic-A";
			request.VerifyReplacedDestination = (path, stamp) => fileSystem.BackupMatchesCapturedStamp;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(fileSystem.RollbackCalled);
			Assert.IsTrue(fileSystem.Files.Contains(fileSystem.RecoveryPath));
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "destination.concurrent-change" &&
				item.Path == fileSystem.RecoveryPath));
		}

		[TestMethod]
		public void Unchanged_destination_completes_guarded_replace_with_and_without_user_backup() {
			foreach (bool createBackup in new[] { false, true }) {
				var fileSystem = ExistingDestinationFileSystem();
				SafeSaveRequest request = Request(fileSystem);
				request.Options.CreateBackup = createBackup;
				request.CaptureDestinationStamp = path => "classic-A";
				request.VerifyReplacedDestination = (path, stamp) => true;

				SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

				Assert.IsTrue(outcome.Success, outcome.Report.ToString());
				Assert.IsTrue(fileSystem.CompleteGuardedReplaceCalled);
				Assert.AreEqual(createBackup, fileSystem.LastGuardWasUserBackup);
			}
		}

		[TestMethod]
		public void Concurrent_change_rollback_failure_preserves_recovery_artifacts() {
			var fileSystem = ExistingDestinationFileSystem();
			fileSystem.BackupMatchesCapturedStamp = false;
			fileSystem.ThrowOnRollback = true;
			SafeSaveRequest request = Request(fileSystem);
			request.CaptureDestinationStamp = path => "classic-A";
			request.VerifyReplacedDestination = (path, stamp) => false;

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(request);

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "destination.concurrent-recovery-failed"));
			Assert.IsTrue(fileSystem.Files.Contains(fileSystem.LastOperationBackupPath));
			Assert.IsTrue(fileSystem.Files.Contains(Path.GetFullPath("archive.grf")));
		}

		private static void UpdateMaximum(ref int maximum, int value) {
			int observed;
			do {
				observed = Volatile.Read(ref maximum);
				if (value <= observed) return;
			} while (Interlocked.CompareExchange(ref maximum, value, observed) != observed);
		}

		private static RecordingFileSystem ExistingDestinationFileSystem() {
			var fileSystem = new RecordingFileSystem();
			fileSystem.Files.Add(Path.GetFullPath("archive.grf"));
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
			public long LengthValue = 1;
			public bool ThrowOnReplace;
			public bool ThrowOnReplaceAndRemoveDestination;
			public bool ThrowOnMove;
			public bool ThrowOnMoveAndCreateDestination;
			public ReplaceFailureState ReplaceFailure;
			public int DeleteTemporaryCount;
			public int MoveCount;
			public int ReplaceCount;
			public string LastBackupPath;
			public string LastTemporaryPath;
			public string LastDestinationPath;
			public string ResidualPreviousBackupPath;
			public string RetainedPreviousBackupPathOnFailure;
			public bool BackupUnavailableOnFailure;
			public bool BackupMatchesCapturedStamp = true;
			public bool RollbackCalled;
			public bool ThrowOnRollback;
			public bool CompleteGuardedReplaceCalled;
			public bool LastGuardWasUserBackup;
			public string LastOperationBackupPath;
			public string RecoveryPath;

			public bool Exists(string path) => Files.Contains(path);
			public long Length(string path) => LengthValue;
			public long AvailableFreeSpace(string directory) => FreeSpace;

			public void DeleteOwnedTemporary(string path) {
				DeleteTemporaryCount++;
				DeletedPaths.Add(path);
				Files.Remove(path);
			}

			public void MoveNew(string temporaryPath, string destinationPath) {
				MoveCount++;
				LastTemporaryPath = temporaryPath;
				LastDestinationPath = destinationPath;
				if (ThrowOnMove || ThrowOnMoveAndCreateDestination) {
					if (ThrowOnMoveAndCreateDestination) Files.Add(destinationPath);
					throw new IOException("ambiguous move failure");
				}
				Files.Remove(temporaryPath);
				Files.Add(destinationPath);
			}

			public string ReplaceExisting(string temporaryPath, string destinationPath, string backupPath) {
				ReplaceCount++;
				LastTemporaryPath = temporaryPath;
				LastDestinationPath = destinationPath;
				LastBackupPath = backupPath;
				if (ThrowOnReplace) throw new IOException("promotion failed");
				if (ThrowOnReplaceAndRemoveDestination) {
					Files.Remove(destinationPath);
					throw new IOException("unexpected partial promotion failure");
				}
				if (ReplaceFailure != ReplaceFailureState.None) {
					if (ReplaceFailure == ReplaceFailureState.DestinationMissingTemporaryOnly ||
						ReplaceFailure == ReplaceFailureState.DestinationMissingBackupAndTemporary) {
						Files.Remove(destinationPath);
					}
					if (ReplaceFailure == ReplaceFailureState.DestinationMissingBackupAndTemporary && backupPath != null) {
						if (BackupUnavailableOnFailure) Files.Remove(backupPath);
						else Files.Add(backupPath);
						Files.Add(destinationPath);
						throw new SafeSavePromotionException("replace failed; backup restored", new IOException("replace failed"), false,
							"promotion.restored-backup", temporaryPath, RetainedPreviousBackupPathOnFailure,
							BackupUnavailableOnFailure ? null : backupPath);
					}
					if (ReplaceFailure == ReplaceFailureState.DestinationMissingTemporaryOnly) {
						Files.Remove(temporaryPath);
						Files.Add(destinationPath);
						throw new SafeSavePromotionException("replace failed; replacement restored", new IOException("replace failed"), true,
							"promotion.recovered-replacement", destinationPath, RetainedPreviousBackupPathOnFailure);
					}
					throw new SafeSavePromotionException("replace failed; destination intact", new IOException("replace failed"), true,
						"promotion.destination-intact", destinationPath, RetainedPreviousBackupPathOnFailure);
				}
				Files.Remove(temporaryPath);
				Files.Add(destinationPath);
				if (backupPath != null) Files.Add(backupPath);
				return ResidualPreviousBackupPath;
			}

			public SafeSaveReplaceResult ReplaceExistingGuarded(string temporaryPath, string destinationPath,
				string operationBackupPath, bool userBackupRequested) {
				LastGuardWasUserBackup = userBackupRequested;
				LastOperationBackupPath = operationBackupPath;
				string retainedPrevious = ReplaceExisting(temporaryPath, destinationPath, operationBackupPath);
				return new SafeSaveReplaceResult(operationBackupPath, retainedPrevious, userBackupRequested);
			}

			public void CompleteGuardedReplace(SafeSaveReplaceResult replaceResult) {
				CompleteGuardedReplaceCalled = true;
				if (!replaceResult.UserBackupRequested) Files.Remove(replaceResult.OperationBackupPath);
			}

			public string RollbackConcurrentReplacement(string destinationPath, SafeSaveReplaceResult replaceResult) {
				RollbackCalled = true;
				RecoveryPath = destinationPath + ".concurrent-recovery-safe-save-test";
				if (ThrowOnRollback) throw new SafeSaveConcurrentRecoveryException(
					"forced rollback failure", RecoveryPath, replaceResult.OperationBackupPath, new IOException("forced"));
				Files.Add(RecoveryPath);
				Files.Remove(replaceResult.OperationBackupPath);
				Files.Add(destinationPath);
				return RecoveryPath;
			}
		}

		private sealed class MutableIdentityResolver : ISafeSavePathIdentityResolver {
			internal readonly Dictionary<string, int> CallsByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			internal int TotalCalls;

			public string Resolve(string path) {
				string fullPath = Path.GetFullPath(path);
				int count;
				CallsByPath.TryGetValue(fullPath, out count);
				CallsByPath[fullPath] = count + 1;
				TotalCalls++;
				return fullPath + "#identity-" + (count + 1);
			}
		}

		private enum ReplaceFailureState {
			None,
			DestinationMissingTemporaryOnly,
			DestinationMissingBackupAndTemporary,
			DestinationAndTemporary
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
		public void Destination_stamp_equality_requires_matching_file_identity_when_available() {
			byte[] header = new byte[46];
			var first = new SafeSaveDestinationStamp(12, 34, header, true, 7, 8, 9);
			var same = new SafeSaveDestinationStamp(12, 34, (byte[])header.Clone(), true, 7, 8, 9);
			var differentFile = new SafeSaveDestinationStamp(12, 34, (byte[])header.Clone(), true, 7, 8, 10);

			Assert.IsTrue(first.Matches(same));
			Assert.IsFalse(first.Matches(differentFile));
		}

		[TestMethod]
		public void Destination_stamp_without_identity_is_conservative_but_matches_identical_observations() {
			byte[] header = new byte[46];
			header[3] = 5;
			var first = new SafeSaveDestinationStamp(12, 34, header, false, 0, 0, 0);
			var same = new SafeSaveDestinationStamp(12, 34, (byte[])header.Clone(), false, 0, 0, 0);
			var changed = new SafeSaveDestinationStamp(12, 35, (byte[])header.Clone(), false, 0, 0, 0);

			Assert.IsTrue(first.Matches(same));
			Assert.IsFalse(first.Matches(changed));
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
		public void MoveNew_exception_with_partial_destination_marks_existing_temporary_as_not_deletable() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			File.WriteAllText(temporary, "validated-replacement");
			SafeSavePromotionException failure = null;
			var fileSystem = new SafeSaveFileSystem(
				(temp, dest, backup, ignoreMetadata) => { throw new NotSupportedException(); },
				(source, target, overwrite) => { throw new NotSupportedException(); },
				(temp, dest) => {
					File.WriteAllText(dest, "partial-or-third-party");
					throw new IOException("ambiguous move");
				});

			try {
				fileSystem.MoveNew(temporary, destination);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.IsFalse(failure.CanDeleteTemporary);
			Assert.AreEqual("promotion.move-ambiguous", failure.RecoveryCode);
			Assert.AreEqual("validated-replacement", File.ReadAllText(temporary));
		}

		[TestMethod]
		public void MoveNew_exception_without_destination_marks_existing_temporary_as_not_deletable() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			File.WriteAllText(temporary, "validated-replacement");
			SafeSavePromotionException failure = null;
			var fileSystem = new SafeSaveFileSystem(
				(temp, dest, backup, ignoreMetadata) => { throw new NotSupportedException(); },
				(source, target, overwrite) => { throw new NotSupportedException(); },
				(temp, dest) => { throw new IOException("ambiguous move"); });

			try {
				fileSystem.MoveNew(temporary, destination);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.IsFalse(failure.CanDeleteTemporary);
			Assert.AreEqual("validated-replacement", File.ReadAllText(temporary));
			Assert.IsFalse(File.Exists(destination));
		}

		[TestMethod]
		public void ReplaceExisting_promotes_temp_and_backs_up_original() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");

			string retained = new SafeSaveFileSystem().ReplaceExisting(temporary, destination, backup);

			Assert.AreEqual("replacement", File.ReadAllText(destination));
			Assert.AreEqual("original", File.ReadAllText(backup));
			Assert.IsFalse(File.Exists(temporary));
			Assert.IsNull(retained);
		}

		[TestMethod]
		public void Replace_failure_restores_previous_backup_when_no_new_backup_exists() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			File.WriteAllText(backup, "previous-backup");

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					throw new IOException("replace failed");
				}).ReplaceExisting(temporary, destination, backup);
				Assert.Fail("ReplaceExisting should have propagated the replacement failure.");
			}
			catch (IOException) {
			}

			Assert.AreEqual("previous-backup", File.ReadAllText(backup));
			Assert.AreEqual(0, Directory.GetFiles(_temporaryDirectory, "*.previous-safe-save-*").Length);
		}

		[TestMethod]
		public void Replace_failure_preserves_new_backup_and_unique_previous_backup() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			File.WriteAllText(backup, "previous-backup");

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					File.WriteAllText(backupPath, "new-backup");
					throw new IOException("replace failed after backup creation");
				}).ReplaceExisting(temporary, destination, backup);
				Assert.Fail("ReplaceExisting should have propagated the replacement failure.");
			}
			catch (IOException) {
			}

			Assert.AreEqual("new-backup", File.ReadAllText(backup));
			string previous = Directory.GetFiles(_temporaryDirectory, "*.previous-safe-save-*").Single();
			Assert.AreEqual("previous-backup", File.ReadAllText(previous));
		}

		[TestMethod]
		public void Partial_replace_with_only_temporary_left_promotes_it_but_returns_failure() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			SafeSavePromotionException failure = null;

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					File.Delete(dest);
					throw new IOException("1176-style partial failure");
				}).ReplaceExisting(temporary, destination, null);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.AreEqual("promotion.recovered-replacement", failure.RecoveryCode);
			Assert.IsTrue(failure.CanDeleteTemporary);
			Assert.AreEqual("replacement", File.ReadAllText(destination));
			Assert.IsFalse(File.Exists(temporary));
		}

		[TestMethod]
		public void Partial_replace_with_new_backup_restores_destination_and_preserves_both_artifacts() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			SafeSavePromotionException failure = null;

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					File.Move(dest, backupPath);
					throw new IOException("1177-style partial failure");
				}).ReplaceExisting(temporary, destination, backup);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.AreEqual("promotion.restored-backup", failure.RecoveryCode);
			Assert.IsFalse(failure.CanDeleteTemporary);
			Assert.AreEqual("original", File.ReadAllText(destination));
			Assert.AreEqual("original", File.ReadAllText(backup));
			Assert.AreEqual("replacement", File.ReadAllText(temporary));
		}

		[TestMethod]
		public void Backup_recreation_failure_leaves_restored_destination_whole_and_temporary_retained() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original-complete");
			File.WriteAllText(temporary, "replacement-complete");
			SafeSavePromotionException failure = null;

			try {
				new SafeSaveFileSystem(
					(temp, dest, backupPath, ignoreMetadataErrors) => {
						File.Move(dest, backupPath);
						throw new IOException("1177-style partial failure");
					},
					(source, target, overwrite) => {
						File.WriteAllText(target, "partial");
						throw new IOException("disk full while recreating backup");
					})
					.ReplaceExisting(temporary, destination, backup);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.AreEqual("promotion.restored-backup-recreation-failed", failure.RecoveryCode);
			Assert.IsFalse(failure.CanDeleteTemporary);
			Assert.IsNull(failure.ActualBackupPath);
			Assert.AreEqual("original-complete", File.ReadAllText(destination));
			Assert.AreEqual("replacement-complete", File.ReadAllText(temporary));
			Assert.IsFalse(File.Exists(backup));
			Assert.AreEqual(0, Directory.GetFiles(_temporaryDirectory, "*.backup-recreate-safe-save-*.tmp").Length);
		}

		[TestMethod]
		public void Replace_failure_with_destination_intact_marks_residual_temporary_as_safe_to_clean() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			SafeSavePromotionException failure = null;

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					throw new IOException("replace failed before mutation");
				}).ReplaceExisting(temporary, destination, null);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.AreEqual("promotion.destination-intact", failure.RecoveryCode);
			Assert.IsTrue(failure.CanDeleteTemporary);
			Assert.AreEqual("original", File.ReadAllText(destination));
			Assert.AreEqual("replacement", File.ReadAllText(temporary));
		}

		[TestMethod]
		public void Failed_replace_exposes_retained_previous_backup_path() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string temporary = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string backup = destination + ".bak";
			File.WriteAllText(destination, "original");
			File.WriteAllText(temporary, "replacement");
			File.WriteAllText(backup, "previous-backup");
			SafeSavePromotionException failure = null;

			try {
				new SafeSaveFileSystem((temp, dest, backupPath, ignoreMetadataErrors) => {
					File.WriteAllText(backupPath, "new-backup");
					throw new IOException("replace failed after producing backup");
				}).ReplaceExisting(temporary, destination, backup);
			}
			catch (SafeSavePromotionException exception) {
				failure = exception;
			}

			Assert.IsNotNull(failure);
			Assert.IsNotNull(failure.RetainedPreviousBackupPath);
			Assert.AreEqual("previous-backup", File.ReadAllText(failure.RetainedPreviousBackupPath));
		}
	}
}
