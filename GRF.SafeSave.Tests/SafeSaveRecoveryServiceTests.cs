using System;
using System.IO;
using System.Linq;
using GRF.Core;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class SafeSaveRecoveryServiceTests {
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
		public void FindOwnedTemporaries_returns_only_exact_safe_save_names() {
			string destination = Path.Combine(_temporaryDirectory, "archive.grf");
			string owned = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			File.WriteAllBytes(owned, new byte[] { 1 });
			File.WriteAllBytes(destination + ".safe-save-0123456789ABCDEF0123456789ABCDEF.tmp", new byte[] { 2 });
			File.WriteAllBytes(destination + ".safe-save-0123456789abcdef.tmp", new byte[] { 3 });
			File.WriteAllBytes(Path.Combine(_temporaryDirectory, "other.grf.safe-save-0123456789abcdef0123456789abcdef.tmp"), new byte[] { 4 });

			string[] found = new SafeSaveRecoveryService().FindOwnedTemporaries(destination).ToArray();

			CollectionAssert.AreEqual(new[] { owned }, found);
		}

		[TestMethod]
		public void DeleteOwnedTemporary_removes_only_an_exact_discovered_path() {
			string destination = Path.Combine(_temporaryDirectory, "delete.grf");
			string owned = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmp";
			string similar = destination + ".safe-save-0123456789abcdef0123456789abcdef.tmpx";
			File.WriteAllBytes(owned, new byte[] { 1 });
			File.WriteAllBytes(similar, new byte[] { 2 });
			var service = new SafeSaveRecoveryService();

			service.DeleteOwnedTemporary(destination, owned);

			Assert.IsFalse(File.Exists(owned));
			try {
				service.DeleteOwnedTemporary(destination, similar);
				Assert.Fail("A similar but non-owned path must be rejected.");
			}
			catch (InvalidOperationException) {
			}
			Assert.IsTrue(File.Exists(similar));
		}

		[TestMethod]
		public void Invalid_backup_is_rejected_without_changing_destination() {
			string destination = CreateGrf("current.grf", "data\\current.txt", new byte[] { 1 });
			string backup = Path.Combine(_temporaryDirectory, "current.grf.bak");
			File.WriteAllBytes(backup, new byte[12]);
			byte[] before = File.ReadAllBytes(destination);

			SafeSaveValidationReport report = new SafeSaveRecoveryService().RestoreBackup(destination, backup);

			Assert.IsTrue(report.HasErrors);
			CollectionAssert.AreEqual(before, File.ReadAllBytes(destination));
			Assert.IsTrue(File.Exists(backup));
			Assert.IsFalse(File.Exists(destination + ".restore-point"));
		}

		[TestMethod]
		public void Valid_restore_keeps_restore_point_until_final_validation_succeeds() {
			string destination = CreateGrf("restore.grf", "data\\current.txt", new byte[] { 1 });
			string backup = CreateGrf("restore.grf.bak", "data\\backup.txt", new byte[] { 2 });
			string restorePoint = destination + ".restore-point";
			int validationCount = 0;
			var validator = new SafeGrfValidator();
			var service = new SafeSaveRecoveryService((path, manifest) => {
				validationCount++;
				if (string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)) {
					Assert.IsTrue(File.Exists(restorePoint), "The current destination must remain recoverable during final validation.");
				}
				return validator.Validate(path, manifest);
			});

			SafeSaveValidationReport report = service.RestoreBackup(destination, backup);

			Assert.IsFalse(report.HasErrors, report.ToString());
			Assert.AreEqual(2, validationCount);
			Assert.IsTrue(File.Exists(backup), "Restoration must not consume the user's backup.");
			Assert.IsFalse(File.Exists(restorePoint));
			using (var restored = new GrfHolder(destination)) {
				Assert.IsNotNull(restored.FileTable.TryGet("data\\backup.txt"));
			}
		}

		[TestMethod]
		public void Destination_changed_during_backup_validation_is_not_overwritten() {
			string destination = CreateGrf("concurrent-restore.grf", "data\\current.txt", new byte[] { 1 });
			string backup = CreateGrf("concurrent-restore.grf.bak", "data\\backup.txt", new byte[] { 2 });
			string concurrent = CreateGrf("concurrent-owner.grf", "data\\concurrent.txt", new byte[] { 3 });
			byte[] concurrentBytes = File.ReadAllBytes(concurrent);
			var validator = new SafeGrfValidator();
			var service = new SafeSaveRecoveryService((path, manifest) => {
				SafeSaveValidationReport result = validator.Validate(path, manifest);
				if (!string.Equals(path, destination, StringComparison.OrdinalIgnoreCase)) {
					File.Copy(concurrent, destination, true);
				}
				return result;
			});

			SafeSaveValidationReport report = service.RestoreBackup(destination, backup);

			Assert.IsTrue(report.HasErrors);
			Assert.IsTrue(report.Items.Any(item => item.Code == "recovery.destination-changed"), report.ToString());
			CollectionAssert.AreEqual(concurrentBytes, File.ReadAllBytes(destination));
			Assert.IsFalse(File.Exists(destination + ".restore-point"));
		}

		private string CreateGrf(string fileName, string relativePath, byte[] data) {
			string path = Path.Combine(_temporaryDirectory, fileName);
			using (var holder = new GrfHolder()) {
				holder.New(path);
				holder.Commands.AddFile(relativePath, data);
				holder.SaveAs(path);
			}
			return path;
		}
	}
}
