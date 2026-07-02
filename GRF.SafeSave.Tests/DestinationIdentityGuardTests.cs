using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using GRF.Core.SafeSave;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class DestinationIdentityGuardTests {
		private string _directory;

		[TestInitialize]
		public void Initialize() {
			_directory = Path.Combine(Path.GetTempPath(), "grf-identity-guard-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_directory);
		}

		[TestCleanup]
		public void Cleanup() {
			if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
		}

		[TestMethod]
		public void Event_horizon_swap_after_validation_is_rolled_back_with_backup_on_and_off() {
			foreach (bool backup in new[] { false, true }) {
				string destination = Path.Combine(_directory, "event-swap-" + backup + ".grf");
				File.WriteAllBytes(destination, Header("Master of Magic\0", 0x200));
				if (backup) File.WriteAllText(destination + ".bak", "previous-user-backup");
				int replaceCount = 0;
				var fileSystem = new SafeSaveFileSystem((source, target, operationBackup, ignore) => {
					if (++replaceCount == 1) SwapDestination(target, Header("Event Horizon\0RL", 0x200));
					File.Replace(source, target, operationBackup, ignore);
				});

				SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(destination, backup));

				Assert.IsFalse(outcome.Success);
				CollectionAssert.AreEqual(Header("Event Horizon\0RL", 0x200), File.ReadAllBytes(destination),
					(outcome.Error == null ? outcome.Report.ToString() : outcome.Error.ToString()));
				Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "destination.concurrent-change"));
				string recovery = outcome.Report.Items.Single(item => item.Code == "destination.concurrent-change").Path;
				CollectionAssert.AreEqual(Header("Master of Magic\0", 0x200), File.ReadAllBytes(recovery));
				if (backup) Assert.IsTrue(Directory.GetFiles(_directory, "*.previous-safe-save-*").Any(),
					"The user's prior .bak must survive a failed guarded save.");
			}
		}

		[TestMethod]
		public void Different_classic_file_with_identical_bytes_is_detected_by_windows_file_identity() {
			if (Environment.OSVersion.Platform != PlatformID.Win32NT) Assert.Inconclusive("Windows file identity test.");
			string destination = Path.Combine(_directory, "classic-identity.grf");
			byte[] classic = Header("Master of Magic\0", 0x200);
			File.WriteAllBytes(destination, classic);
			DateTime timestamp = File.GetLastWriteTimeUtc(destination);
			int replaceCount = 0;
			var fileSystem = new SafeSaveFileSystem((source, target, operationBackup, ignore) => {
				if (++replaceCount == 1) {
					SwapDestination(target, classic);
					File.SetLastWriteTimeUtc(target, timestamp);
				}
				File.Replace(source, target, operationBackup, ignore);
			});

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(destination, false));

			Assert.IsFalse(outcome.Success, "Matching size, time and header must not hide a different NTFS file identity.");
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "destination.concurrent-change"), outcome.Report.ToString());
		}

		[TestMethod]
		public void Normal_guarded_replace_succeeds_and_removes_internal_guard_backup() {
			string destination = Path.Combine(_directory, "normal.grf");
			File.WriteAllBytes(destination, Header("Master of Magic\0", 0x200));

			SafeSaveOutcome outcome = new SafeSaveCoordinator(new SafeSaveFileSystem()).Execute(Request(destination, false));

			Assert.IsTrue(outcome.Success, outcome.Report.ToString());
			Assert.IsFalse(Directory.GetFiles(_directory, "*.guard-safe-save-*.bak").Any());
		}

		[TestMethod]
		public void Rollback_failure_preserves_validated_destination_and_exact_operation_backup() {
			string destination = Path.Combine(_directory, "rollback-failure.grf");
			File.WriteAllBytes(destination, Header("Master of Magic\0", 0x200));
			int replaceCount = 0;
			var fileSystem = new SafeSaveFileSystem((source, target, operationBackup, ignore) => {
				if (++replaceCount == 1) {
					SwapDestination(target, Header("Event Horizon\0RL", 0x200));
					File.Replace(source, target, operationBackup, ignore);
					return;
				}
				throw new IOException("forced rollback failure");
			});

			SafeSaveOutcome outcome = new SafeSaveCoordinator(fileSystem).Execute(Request(destination, false));

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "destination.concurrent-recovery-failed"), outcome.Report.ToString());
			CollectionAssert.AreEqual(Header("Master of Magic\0", 0x200), File.ReadAllBytes(destination));
			Assert.IsTrue(Directory.GetFiles(_directory, "*.guard-safe-save-*.bak").Any());
		}

		private static SafeSaveRequest Request(string destination, bool backup) {
			return new SafeSaveRequest {
				DestinationPath = destination,
				Options = new SafeSaveOptions { CreateBackup = backup },
				EstimatedLength = 46,
				WriteTemporary = path => File.WriteAllBytes(path, Header("Master of Magic\0", 0x200)),
				ValidateTemporary = path => new SafeSaveValidationReport(),
				CaptureDestinationStamp = path => SafeSaveDestinationStamp.CaptureGrf(path),
				VerifyReplacedDestination = (path, stamp) => {
					SafeSaveDestinationStamp actual = SafeSaveDestinationStamp.CaptureGrf(path);
					return actual.IsEditable && ((SafeSaveDestinationStamp)stamp).Matches(actual);
				}
			};
		}

		private static void SwapDestination(string destination, byte[] replacement) {
			string swap = destination + ".external-" + Guid.NewGuid().ToString("N");
			File.WriteAllBytes(swap, replacement);
			File.Delete(destination);
			File.Move(swap, destination);
		}

		private static byte[] Header(string magic, int version) {
			byte[] bytes = new byte[46];
			byte[] magicBytes = System.Text.Encoding.ASCII.GetBytes(magic);
			Buffer.BlockCopy(magicBytes, 0, bytes, 0, Math.Min(16, magicBytes.Length));
			Buffer.BlockCopy(BitConverter.GetBytes(version), 0, bytes, 42, 4);
			return bytes;
		}
	}
}
