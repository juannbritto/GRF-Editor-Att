using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GRF.ContainerFormat;
using GRF.Core;
using GRF.Core.SafeSave;
using GRF.GrfSystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Utilities.Services;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class GrfHolderSafeSaveIntegrationTests {
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
		public void Save_replaces_existing_grf_through_validated_safe_save() {
			string path = CreateGrf("save.grf", "data\\original.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\original.txt", new byte[] { 9, 8, 7 });
				holder.Commands.AddFile("data\\added.txt", new byte[] { 4, 5, 6 });
				holder.Commands.Rename("data\\added.txt", "data\\renamed.txt");
				holder.Commands.AddFile("data\\deleted.txt", new byte[] { 0 });
				holder.Commands.RemoveFile("data\\deleted.txt");
				result = holder.Save();
			}

			Assert.IsTrue(result.Success, result.Error == null ? "Save failed." : result.Error.ToString());
			Assert.IsNotNull(result.SafeSaveReport);
			Assert.IsFalse(result.SafeSaveReport.HasErrors, result.SafeSaveReport.ToString());
			Assert.AreEqual(path + ".bak", result.BackupFileName);
			Assert.IsTrue(File.Exists(result.BackupFileName));
			CollectionAssert.AreEqual(originalHash, Hash(result.BackupFileName));
			using (var reopened = new GrfHolder(path)) {
				CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, reopened.FileTable["data\\original.txt"].GetDecompressedData());
				CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, reopened.FileTable["data\\renamed.txt"].GetDecompressedData());
				Assert.IsFalse(reopened.FileTable.ContainsFile("data\\added.txt"));
				Assert.IsFalse(reopened.FileTable.ContainsFile("data\\deleted.txt"));
			}
		}

		[TestMethod]
		public void SaveAs_backs_up_existing_destination_and_reopens_promoted_archive() {
			string source = CreateGrf("source.grf", "data\\source.txt", new byte[] { 1 });
			string destination = CreateGrf("destination.grf", "data\\old.txt", new byte[] { 2 });
			byte[] destinationHash = Hash(destination);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(source)) {
				holder.Commands.AddFile("data\\new.txt", new byte[] { 3 });
				result = holder.SaveAs(destination);
				Assert.AreEqual(destination, holder.FileName);
			}

			AssertSafeSuccess(result, SavingMode.FileCopy);
			CollectionAssert.AreEqual(destinationHash, Hash(destination + ".bak"));
			using (var reopened = new GrfHolder(destination)) {
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\source.txt"));
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\new.txt"));
			}
		}

		[TestMethod]
		public void SaveAs_rejects_protected_existing_destination_before_temporary_write() {
			string source = CreateGrf("protected-save-as-source.grf", "data\\source.txt", new byte[] { 1 });
			string destination = CreateGrf("protected-save-as-destination.grf", "data\\protected.txt", new byte[] { 2 });
			using (FileStream stream = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None)) {
				byte[] magic = Encoding.ASCII.GetBytes(GrfStrings.EventHorizon);
				stream.Write(magic, 0, magic.Length);
			}
			byte[] destinationHash = Hash(destination);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(source)) {
				holder.Commands.AddFile("data\\new.txt", new byte[] { 3 });
				result = holder.SaveAs(destination);
			}

			Assert.IsFalse(result.Success);
			Assert.IsInstanceOfType(result.Error, typeof(SafeSaveFormatReadOnlyException));
			Assert.AreEqual("event-horizon", ((SafeSaveFormatReadOnlyException)result.Error).ReasonCode);
			Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Phase == SafeSavePhase.Preflight &&
				item.Code == "format.not-editable" && item.Message == "event-horizon"));
			Assert.IsFalse(result.RequiresReload);
			CollectionAssert.AreEqual(destinationHash, Hash(destination));
			Assert.IsFalse(File.Exists(destination + ".bak"));
			Assert.AreEqual(0, Directory.GetFiles(_temporaryDirectory,
				Path.GetFileName(destination) + ".safe-save-*.tmp").Length);
		}

		[TestMethod]
		public void Save_blocks_classic_destination_changed_to_event_horizon_before_promotion() {
			string path = CreateGrf("destination-swap.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			var validatorEntered = new ManualResetEventSlim(false);
			var allowValidation = new ManualResetEventSlim(false);

			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 4, 5, 6 });
				holder.Container.SafeSaveValidator = (temporary, manifest) => {
					validatorEntered.Set();
					if (!allowValidation.Wait(10000)) throw new TimeoutException("Destination swap was not released.");
					return new SafeGrfValidator().Validate(temporary, manifest);
				};
				Task<ContainerSaveResult> saving = Task.Run(() => holder.Save());
				Assert.IsTrue(validatorEntered.Wait(10000), "Temporary writer did not reach validation.");
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None)) {
					byte[] magic = Encoding.ASCII.GetBytes(GrfStrings.EventHorizon);
					stream.Write(magic, 0, magic.Length);
				}
				byte[] protectedHash = Hash(path);
				allowValidation.Set();
				Assert.IsTrue(saving.Wait(15000), "Safe save did not finish after the destination swap.");
				ContainerSaveResult result = saving.Result;

				Assert.IsFalse(result.Success);
				Assert.IsInstanceOfType(result.Error, typeof(SafeSaveFormatReadOnlyException));
				Assert.AreEqual("event-horizon", ((SafeSaveFormatReadOnlyException)result.Error).ReasonCode);
				Assert.IsFalse(result.RequiresReload);
				CollectionAssert.AreEqual(protectedHash, Hash(path));
			}

			Assert.IsFalse(File.Exists(path + ".bak"));
			Assert.AreEqual(0, Directory.GetFiles(_temporaryDirectory,
				Path.GetFileName(path) + ".safe-save-*.tmp").Length);
		}

		[TestMethod]
		public void RepackSource_uses_validated_atomic_replace_and_backup() {
			string path = CreateGrf("source-archive.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				result = holder.Container.Save(null, null, SavingMode.RepackSource, SyncMode.Synchronous);
			}

			AssertSafeSuccess(result, SavingMode.RepackSource);
			CollectionAssert.AreEqual(originalHash, Hash(path + ".bak"));
			using (var reopened = new GrfHolder(path)) {
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reopened.FileTable["data\\base.txt"].GetDecompressedData());
			}
		}

		[TestMethod]
		public void RepackSource_promotion_failure_preserves_original_archive() {
			string path = CreateGrf("source-archive-failure.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());
				result = holder.Container.Save(null, null, SavingMode.RepackSource, SyncMode.Synchronous);
			}

			Assert.IsFalse(result.Success);
			CollectionAssert.AreEqual(originalHash, Hash(path));
			Assert.IsFalse(File.Exists(path + ".bak"));
		}

		[TestMethod]
		public void Thor_repack_propagates_inner_safe_save_failure_and_preserves_original() {
			string seed = CreateGrf("thor-seed.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			string thorPath = Path.Combine(_temporaryDirectory, "patch.thor");
			using (var holder = new GrfHolder(seed)) {
				ContainerSaveResult create = holder.SaveAs(thorPath);
				Assert.IsTrue(create.Success, create.Error == null ? "THOR creation failed." : create.Error.ToString());
			}
			byte[] originalHash = Hash(thorPath);

			using (var holder = new GrfHolder(thorPath)) {
				holder.Container.SafeSaveValidator = (temporary, manifest) => {
					var report = new SafeSaveValidationReport();
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "forced");
					return report;
				};
				ContainerSaveResult result = holder.Repack();

				Assert.IsFalse(result.Success);
				Assert.IsNotNull(result.SafeSaveReport);
			}

			CollectionAssert.AreEqual(originalHash, Hash(thorPath));
		}

		[TestMethod]
		public void Thor_outer_failure_after_inner_repack_never_changes_original_to_grf() {
			string seed = CreateGrf("thor-outer-seed.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			string thorPath = Path.Combine(_temporaryDirectory, "outer-failure.thor");
			using (var holder = new GrfHolder(seed)) Assert.IsTrue(holder.SaveAs(thorPath).Success);
			byte[] originalHash = Hash(thorPath);

			using (var holder = new GrfHolder(thorPath)) {
				holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());
				ContainerSaveResult result = holder.Repack();
				Assert.IsFalse(result.Success);
			}

			CollectionAssert.AreEqual(originalHash, Hash(thorPath));
			Assert.AreEqual("ASSF (C) 2007 Aeomin DEV", Encoding.ASCII.GetString(File.ReadAllBytes(thorPath), 0, 24));
		}

		[TestMethod]
		public void Successful_thor_repack_keeps_thor_magic() {
			string seed = CreateGrf("thor-success-seed.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			string thorPath = Path.Combine(_temporaryDirectory, "success.thor");
			using (var holder = new GrfHolder(seed)) Assert.IsTrue(holder.SaveAs(thorPath).Success);

			using (var holder = new GrfHolder(thorPath)) {
				ContainerSaveResult result = holder.Repack();
				Assert.IsTrue(result.Success, (result.Error == null ? "THOR repack failed." : result.Error.ToString()) +
					Environment.NewLine + result.SafeSaveReport + Environment.NewLine +
					string.Join(Environment.NewLine, result.SafeSaveReport?.Items.Select(item => item.Code + ":" + item.Path) ?? Enumerable.Empty<string>()));
				Assert.IsTrue(holder.FileTable.ContainsFile("root\\data\\base.txt"));
				ContainerSaveResult second = holder.Save();
				Assert.IsTrue(second.Success, second.Error == null ? "Second THOR save failed." : second.Error.ToString());
			}

			Assert.AreEqual("ASSF (C) 2007 Aeomin DEV", Encoding.ASCII.GetString(File.ReadAllBytes(thorPath), 0, 24));
		}

		[TestMethod]
		public void Validation_failure_does_not_write_external_encryption_index() {
			AssertExternalEncryptionIndexWriteIsDeferred(promotionFailure: false);
		}

		[TestMethod]
		public void Promotion_failure_does_not_write_external_encryption_index() {
			AssertExternalEncryptionIndexWriteIsDeferred(promotionFailure: true);
		}

		[TestMethod]
		public void Successful_encrypted_save_writes_external_index_using_promoted_timestamp() {
			string path = CreateGrf("external-index-success.grf", "data\\base.txt", new byte[] { 1 });
			string writtenUid = null;

			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => writtenUid = uid;
				ContainerSaveResult result = holder.Save();

				AssertSafeSuccess(result, SavingMode.FileCopy);
			}

			Assert.AreEqual(File.GetLastWriteTimeUtc(path).ToFileTimeUtc() + "\\files.enc", writtenUid);
		}

		[TestMethod]
		public void External_index_failure_after_promotion_reports_committed_archive_without_reload() {
			string path = CreateGrf("external-index-failure.grf", "data\\base.txt", new byte[] { 1 });
			byte[] originalHash = Hash(path);

			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => { throw new IOException("forced external index failure"); };
				ContainerSaveResult result = holder.Save();

				Assert.IsFalse(result.Success);
				Assert.IsFalse(result.RequiresReload);
				Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "encryption-index.write-failed"));
			}

			Assert.IsFalse(originalHash.SequenceEqual(Hash(path)));
			using (var reopened = new GrfHolder(path)) Assert.IsTrue(reopened.Header.IsEncrypted);
		}

		[TestMethod]
		public void Merge_uses_safe_file_copy_and_preserves_both_archives_logically() {
			string destination = CreateGrf("merge-destination.grf", "data\\base.txt", new byte[] { 1 });
			string added = CreateGrf("merge-added.grf", "data\\added.txt", new byte[] { 2 });
			byte[] originalHash = Hash(destination);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(destination))
			using (var merge = new GrfHolder(added)) {
				result = holder.Merge(merge);
			}

			AssertSafeSuccess(result, SavingMode.FileCopy);
			CollectionAssert.AreEqual(originalHash, Hash(destination + ".bak"));
			using (var reopened = new GrfHolder(destination)) {
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\base.txt"));
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\added.txt"));
			}
		}

		[TestMethod]
		public void MergeAs_protected_destination_restores_exact_pre_call_table() {
			string source = CreateGrf("merge-protected-source.grf", "data\\base.txt", new byte[] { 1 });
			string addition = CreateGrf("merge-protected-addition.grf", "data\\added.txt", new byte[] { 2 });
			string destination = CreateGrf("merge-protected-destination.grf", "data\\protected.txt", new byte[] { 3 });
			using (FileStream stream = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None)) {
				byte[] magic = Encoding.ASCII.GetBytes(GrfStrings.EventHorizon);
				stream.Write(magic, 0, magic.Length);
			}
			byte[] destinationHash = Hash(destination);

			using (var holder = new GrfHolder(source))
			using (var merge = new GrfHolder(addition)) {
				FileEntry baseEntry = holder.FileTable["data\\base.txt"];
				ContainerSaveResult result = holder.MergeAs(destination, merge);

				Assert.IsFalse(result.Success);
				Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "format.not-editable"));
				Assert.AreEqual(1, holder.FileTable.Count);
				Assert.AreSame(baseEntry, holder.FileTable["data\\base.txt"]);
				Assert.IsFalse(holder.FileTable.InternalContains("data\\added.txt"));
			}

			CollectionAssert.AreEqual(destinationHash, Hash(destination));
			Assert.IsFalse(File.Exists(destination + ".bak"));
		}

		[TestMethod]
		public void Merge_validation_failure_restores_exact_pre_call_table() {
			AssertMergeFailureRestoresPreCallTable(promotionFailure: false);
		}

		[TestMethod]
		public void Merge_promotion_failure_restores_exact_pre_call_table() {
			AssertMergeFailureRestoresPreCallTable(promotionFailure: true);
		}

		[TestMethod]
		public void Repack_and_compact_each_use_validated_safe_save() {
			string path = CreateGrf("maintenance.grf", "data\\same.txt", new byte[] { 1, 2, 3 });

			using (var holder = new GrfHolder(path)) {
				ContainerSaveResult repack = holder.Repack();
				AssertSafeSuccess(repack, SavingMode.Repack);
				ContainerSaveResult compact = holder.Compact();
				AssertSafeSuccess(compact, SavingMode.Compact);
			}

			using (var reopened = new GrfHolder(path)) {
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reopened.FileTable["data\\same.txt"].GetDecompressedData());
			}
		}

		[TestMethod]
		public void Korean_cp949_path_round_trips_through_safe_save() {
			Encoding previous = EncodingService.DisplayEncoding;
			Encoding korean = EncodingService.Korean;
			if (korean == null) Assert.Inconclusive("CP949 is unavailable on this .NET Framework runtime.");
			string path = Path.Combine(_temporaryDirectory, "korean.grf");
			const string relativePath = "data\\sprite\\몬스터\\테스트.txt";

			Assert.AreEqual("data\\sprite\\몬스터\\테스트.txt", relativePath);

			try {
				Assert.IsTrue(EncodingService.SetDisplayEncoding(949));
				using (var holder = new GrfHolder()) {
					holder.New(path);
					holder.Commands.AddFile(relativePath, new byte[] { 7, 4, 9 });
					AssertSafeSuccess(holder.SaveAs(path), SavingMode.FileCopy);
				}
				using (var holder = new GrfHolder(path)) {
					holder.Commands.AddFile("data\\extra.txt", new byte[] { 1 });
					AssertSafeSuccess(holder.Save(), SavingMode.FileCopy);
				}
				using (var reopened = new GrfHolder(path)) {
					Assert.IsTrue(reopened.FileTable.ContainsFile(relativePath));
					Assert.AreEqual(relativePath, reopened.FileTable[relativePath].RelativePath);
					CollectionAssert.AreEqual(new byte[] { 7, 4, 9 }, reopened.FileTable[relativePath].GetDecompressedData());
				}
			}
			finally {
				EncodingService.DisplayEncoding = previous;
			}
		}

		[TestMethod]
		public void Event_horizon_holder_is_rejected_before_any_destination_change() {
			string path = CreateGrf("protected.grf", "data\\original.txt", new byte[] { 1 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Header.Magic = GrfStrings.EventHorizon;
				holder.Commands.AddFile("data\\blocked.txt", new byte[] { 2 });
				result = holder.Save();
			}

			Assert.IsFalse(result.Success);
			Assert.IsInstanceOfType(result.Error, typeof(SafeSaveFormatReadOnlyException));
			Assert.IsTrue(result.SafeSaveReport.HasErrors);
			Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "format.not-editable" && item.Message == "event-horizon"));
			CollectionAssert.AreEqual(originalHash, Hash(path));
			Assert.IsFalse(File.Exists(path + ".bak"));
		}

		[TestMethod]
		public void Backup_can_be_disabled_without_bypassing_validation() {
			string path = CreateGrf("no-backup.grf", "data\\original.txt", new byte[] { 1 });
			ContainerSaveResult result;

			using (var holder = new GrfHolder(path)) {
				holder.SafeSaveOptions = new SafeSaveOptions { CreateBackup = false };
				holder.Commands.AddFile("data\\new.txt", new byte[] { 2 });
				result = holder.Save();
			}

			AssertSafeSuccess(result, SavingMode.FileCopy);
			Assert.IsNull(result.BackupFileName);
			Assert.IsFalse(File.Exists(path + ".bak"));
		}

		[TestMethod]
		public void Named_maintenance_and_merge_variants_also_route_through_safe_save() {
			string source = CreateGrf("variants.grf", "data\\base.txt", new byte[] { 1 });
			string addition = CreateGrf("variants-addition.grf", "data\\addition.txt", new byte[] { 2 });
			string merged = Path.Combine(_temporaryDirectory, "merged.grf");
			string repacked = Path.Combine(_temporaryDirectory, "repacked.grf");
			string compacted = Path.Combine(_temporaryDirectory, "compacted.grf");

			using (var holder = new GrfHolder(source))
			using (var added = new GrfHolder(addition)) {
				AssertSafeSuccess(holder.MergeAs(merged, added), SavingMode.FileCopy);
				AssertSafeSuccess(holder.RepackAs(repacked), SavingMode.Repack);
				AssertSafeSuccess(holder.CompactAs(compacted), SavingMode.Compact);
			}

			using (var reopened = new GrfHolder(compacted)) {
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\base.txt"));
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\addition.txt"));
			}
		}

		[TestMethod]
		public void Gpf_quick_save_uses_the_same_safe_save_route() {
			string path = CreateGrf("archive.gpf", "data\\base.txt", new byte[] { 1 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\new.txt", new byte[] { 2 });
				result = holder.Save();
			}

			AssertSafeSuccess(result, SavingMode.FileCopy);
			CollectionAssert.AreEqual(originalHash, Hash(path + ".bak"));
		}

		[TestMethod]
		public void Validation_failure_restores_header_and_entry_write_metadata() {
			string path = CreateGrf("metadata-rollback.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);

			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 4, 5 });
				long fileTableOffset = holder.Header.FileTableOffset;
				int realFilesCount = holder.Container.InternalHeader.RealFilesCount;
				int tableSize = holder.Container.InternalTable.TableSize;
				int tableSizeCompressed = holder.Container.InternalTable.TableSizeCompressed;
				var entryState = holder.FileTable.ToDictionary(entry => entry.RelativePath, entry => new {
					entry.TemporaryOffset,
					entry.TemporarySizeCompressedAlignment,
					entry.NewSizeCompressed,
					entry.NewSizeDecompressed,
					entry.FileExactOffset,
					entry.Offset
				});
				holder.Container.SafeSaveValidator = (temporary, manifest) => {
					var report = new SafeSaveValidationReport();
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "Forced failure.");
					return report;
				};

				ContainerSaveResult result = holder.Save();

				Assert.IsFalse(result.Success);
				Assert.AreEqual(fileTableOffset, holder.Header.FileTableOffset);
				Assert.AreEqual(realFilesCount, holder.Container.InternalHeader.RealFilesCount);
				Assert.AreEqual(tableSize, holder.Container.InternalTable.TableSize);
				Assert.AreEqual(tableSizeCompressed, holder.Container.InternalTable.TableSizeCompressed);
				foreach (FileEntry entry in holder.FileTable) {
					var expected = entryState[entry.RelativePath];
					Assert.AreEqual(expected.TemporaryOffset, entry.TemporaryOffset, entry.RelativePath);
					Assert.AreEqual(expected.TemporarySizeCompressedAlignment, entry.TemporarySizeCompressedAlignment, entry.RelativePath);
					Assert.AreEqual(expected.NewSizeCompressed, entry.NewSizeCompressed, entry.RelativePath);
					Assert.AreEqual(expected.NewSizeDecompressed, entry.NewSizeDecompressed, entry.RelativePath);
					Assert.AreEqual(expected.FileExactOffset, entry.FileExactOffset, entry.RelativePath);
					Assert.AreEqual(expected.Offset, entry.Offset, entry.RelativePath);
				}
				Assert.IsTrue(holder.IsModified);
			}

			CollectionAssert.AreEqual(originalHash, Hash(path));
		}

		[TestMethod]
		public void Validation_failure_restores_encryption_index_membership_entry_state_and_lock() {
			AssertEncryptionRollback(promotionFailure: false);
		}

		[TestMethod]
		public void Promotion_failure_with_intact_destination_restores_state_and_reader() {
			string path = CreateGrf("promotion-rollback.grf", "data\\base.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);

			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 4 });
				long fileTableOffset = holder.Header.FileTableOffset;
				holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());

				ContainerSaveResult result = holder.Save();

				Assert.IsFalse(result.Success);
				Assert.AreEqual(fileTableOffset, holder.Header.FileTableOffset);
				Assert.IsFalse(result.RequiresReload);
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, holder.FileTable["data\\base.txt"].GetDecompressedData());
				Assert.IsTrue(holder.FileTable.ContainsFile("data\\pending.txt"));
			}

			CollectionAssert.AreEqual(originalHash, Hash(path));
		}

		[TestMethod]
		public void Promotion_failure_restores_encryption_index_membership_entry_state_and_lock() {
			AssertEncryptionRollback(promotionFailure: true);
		}

		[TestMethod]
		public void Successful_new_encryption_prepares_manifest_and_reopens_cleanly() {
			string path = CreateGrf("new-encryption-success.grf", "data\\base.txt", new byte[] { 1, 2, 3 });

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => { };
				result = holder.Save();
			}

			AssertSafeSuccess(result, SavingMode.FileCopy);
			using (var reopened = new GrfHolder(path)) {
				Assert.IsTrue(reopened.Header.IsEncrypted);
				CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, reopened.FileTable["data\\base.txt"].GetDecompressedData());
			}
		}

		[TestMethod]
		public void Encryption_index_payload_is_included_in_manifest_validation() {
			string path = CreateGrf("encryption-index-manifest.grf", "data\\base.txt", new byte[] { 1 });
			SafeSaveManifest expected;
			long encryptionOffset;
			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => { };
				AssertSafeSuccess(holder.Save(), SavingMode.FileCopy);
			}
			using (var holder = new GrfHolder(path)) {
				expected = SafeSaveManifest.Capture(holder.Container);
				encryptionOffset = holder.FileTable[GrfStrings.EncryptionFilename].FileExactOffset;
			}

			using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				stream.Position = encryptionOffset;
				int value = stream.ReadByte();
				stream.Position = encryptionOffset;
				stream.WriteByte((byte)(value ^ 0xff));
			}

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(path, expected);
			Assert.IsTrue(report.HasErrors);
			Assert.IsTrue(report.Items.Any(item => item.Path == GrfStrings.EncryptionFilename &&
				(item.Code == "manifest.hash" || item.Code == "validation.exception")),
				string.Join(";", report.Items.Select(item => item.Code + ":" + item.Path)));
		}

		[TestMethod]
		public void Disabling_encryption_removes_hidden_index_and_validates_expected_manifest() {
			string path = CreateGrf("remove-encryption-index.grf", "data\\base.txt", new byte[] { 1 });
			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => { };
				AssertSafeSuccess(holder.Save(), SavingMode.FileCopy);
			}

			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = false;
				holder.Container.SafeSaveEncryptionIndexWriter = uid => { };
				AssertSafeSuccess(holder.Save(), SavingMode.FileCopy);
			}

			using (var reopened = new GrfHolder(path)) {
				Assert.IsFalse(reopened.FileTable.InternalContains(GrfStrings.EncryptionFilename));
			}
		}

		[TestMethod]
		public void Reload_failure_after_promotion_requires_reload_without_rolling_back_disk_result() {
			string path = CreateGrf("reload-failure.grf", "data\\base.txt", new byte[] { 1 });
			byte[] originalHash = Hash(path);

			ContainerSaveResult result;
			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\promoted.txt", new byte[] { 2 });
				holder.Container.SafeSaveReload = (destination, mode, ignoreFileType) =>
					throw new IOException("Forced reload failure.");
				result = holder.Save();
			}

			Assert.IsFalse(result.Success);
			Assert.IsTrue(result.RequiresReload);
			Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "reload.failed"));
			Assert.AreEqual(path + ".bak", result.BackupFileName);
			Assert.IsFalse(string.IsNullOrEmpty(result.TemporaryFileName));
			Assert.IsFalse(File.Exists(result.TemporaryFileName));
			CollectionAssert.AreEqual(originalHash, Hash(path + ".bak"));
			Assert.IsFalse(originalHash.SequenceEqual(Hash(path)));
			using (var reopened = new GrfHolder(path)) {
				Assert.IsTrue(reopened.FileTable.ContainsFile("data\\promoted.txt"));
			}
		}

		[TestMethod]
		public void Failed_lock_restore_is_reported_and_requires_reload() {
			string path = CreateGrf("lock-restore-failure.grf", "data\\base.txt", new byte[] { 1 });
			string source = Path.Combine(_temporaryDirectory, "locked-source.txt");
			File.WriteAllBytes(source, new byte[] { 2, 3 });
			bool previousLockFiles = Settings.LockFiles;

			try {
				Settings.LockFiles = true;
				using (var holder = new GrfHolder(path)) {
					holder.Commands.AddFile("data\\locked.txt", source);
					holder.Container.InternalTable.RestoreLockedFileOpener = lockedPath =>
						throw new IOException("Forced lock reopen failure.");
					holder.Container.SafeSaveValidator = (temporary, manifest) => {
						var report = new SafeSaveValidationReport();
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "Forced failure.");
						return report;
					};

					ContainerSaveResult result = holder.Save();

					Assert.IsFalse(result.Success);
					Assert.IsTrue(result.RequiresReload);
					Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "forced.validation"));
					Assert.IsTrue(result.SafeSaveReport.Items.Any(item => item.Code == "state.restore-failed"));
				}
			}
			finally {
				Settings.LockFiles = previousLockFiles;
			}
		}

		[TestMethod]
		public void CreateFromBufferedFiles_safely_replaces_and_backs_up_existing_grf() {
			string destination = CreateGrf("buffered.grf", "data\\old.txt", new byte[] { 1 });
			byte[] originalHash = Hash(destination);
			byte[] expected = { 8, 6, 7, 5, 3, 0, 9 };
			FileEntry entry = CreateBufferedEntry("buffer-success.bin", "data\\buffered.txt", expected);

			GrfHolder.CreateFromBufferedFiles(destination, new System.Collections.Generic.List<FileEntry> { entry });

			Assert.IsFalse(File.Exists(entry.SourceFilePath));
			CollectionAssert.AreEqual(originalHash, Hash(destination + ".bak"));
			using (var reopened = new GrfHolder(destination)) {
				CollectionAssert.AreEqual(expected, reopened.FileTable["data\\buffered.txt"].GetDecompressedData());
				Assert.IsFalse(reopened.FileTable.ContainsFile("data\\old.txt"));
			}
		}

		[TestMethod]
		public void CreateFromBufferedFiles_validation_failure_preserves_existing_destination() {
			string destination = CreateGrf("buffered-failure.grf", "data\\old.txt", new byte[] { 1, 2 });
			byte[] originalHash = Hash(destination);
			FileEntry entry = CreateBufferedEntry("buffer-failure.bin", "data\\buffered.txt", new byte[] { 3, 4 });
			Func<string, SafeSaveManifest, SafeSaveValidationReport> failValidation = (temporary, manifest) => {
				var report = new SafeSaveValidationReport();
				report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "Forced failure.");
				return report;
			};

			SafeSaveOutcome outcome = GrfHolder.CreateFromBufferedFilesSafely(destination,
				new System.Collections.Generic.List<FileEntry> { entry }, new SafeSaveCoordinator(), failValidation,
				new SafeSaveOptions());

			Assert.IsFalse(outcome.Success);
			Assert.IsTrue(File.Exists(entry.SourceFilePath));
			CollectionAssert.AreEqual(originalHash, Hash(destination));
			Assert.IsFalse(File.Exists(destination + ".bak"));
			using (var reopened = new GrfHolder(destination)) {
				CollectionAssert.AreEqual(new byte[] { 1, 2 }, reopened.FileTable["data\\old.txt"].GetDecompressedData());
			}
		}

		[TestMethod]
		public void CreateFromBufferedFiles_rejects_protected_existing_destination_before_writing() {
			string destination = CreateGrf("buffered-protected.grf", "data\\old.txt", new byte[] { 1, 2 });
			using (FileStream stream = new FileStream(destination, FileMode.Open, FileAccess.Write, FileShare.None)) {
				byte[] magic = Encoding.ASCII.GetBytes(GrfStrings.EventHorizon);
				stream.Write(magic, 0, magic.Length);
			}
			byte[] originalHash = Hash(destination);
			FileEntry entry = CreateBufferedEntry("buffer-protected.bin", "data\\buffered.txt", new byte[] { 3, 4 });

			try {
				GrfHolder.CreateFromBufferedFiles(destination, new System.Collections.Generic.List<FileEntry> { entry });
				Assert.Fail("A protected destination must not be overwritten.");
			}
			catch (SafeSaveFormatReadOnlyException exception) {
				Assert.AreEqual("event-horizon", exception.ReasonCode);
			}

			CollectionAssert.AreEqual(originalHash, Hash(destination));
			Assert.IsFalse(File.Exists(destination + ".bak"));
			Assert.IsTrue(File.Exists(entry.SourceFilePath));
		}

		[TestMethod]
		public void Asynchronous_save_returns_before_manifest_preparation_finishes() {
			string path = CreateGrf("async-responsive.grf", "data\\base.txt", new byte[] { 1 });
			var manifestEntered = new ManualResetEventSlim(false);
			var releaseManifest = new ManualResetEventSlim(false);

			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 2 });
				holder.Container.SafeSaveManifestFactory = container => {
					manifestEntered.Set();
					if (!releaseManifest.Wait(10000)) throw new TimeoutException("manifest gate");
					return SafeSaveManifest.Capture(container);
				};

				ContainerSaveResult result = holder.Save(SyncMode.Asynchronous);
				Assert.IsTrue(manifestEntered.Wait(5000));
				Assert.IsFalse(result.Completed, "Async Save returned only after completing expensive manifest work.");
				releaseManifest.Set();
				Assert.IsTrue(SpinWait.SpinUntil(() => result.Completed, 15000));
				Assert.IsTrue(result.Success, result.Error == null ? "save failed" : result.Error.ToString());
			}
		}

		[TestMethod]
		public void Concurrent_save_is_rejected_before_manifest_or_table_mutation() {
			string path = CreateGrf("concurrent-guard.grf", "data\\base.txt", new byte[] { 1 });
			string mergePath = CreateGrf("concurrent-merge.grf", "data\\must-not-merge.txt", new byte[] { 3 });
			var manifestEntered = new ManualResetEventSlim(false);
			var releaseManifest = new ManualResetEventSlim(false);
			int manifestCalls = 0;

			using (var holder = new GrfHolder(path))
			using (var merge = new GrfHolder(mergePath)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 2 });
				FileEntry baseEntry = holder.FileTable["data\\base.txt"];
				FileEntry pendingEntry = holder.FileTable["data\\pending.txt"];
				holder.Container.SafeSaveManifestFactory = container => {
					Interlocked.Increment(ref manifestCalls);
					manifestEntered.Set();
					if (!releaseManifest.Wait(10000)) throw new TimeoutException("manifest gate");
					return SafeSaveManifest.Capture(container);
				};

				ContainerSaveResult first = holder.Save(SyncMode.Asynchronous);
				Assert.IsTrue(manifestEntered.Wait(5000));
				ContainerSaveResult second = holder.Container.Save(null, merge.Container, SavingMode.FileCopy,
					SyncMode.Synchronous);

				Assert.IsFalse(second.Success);
				Assert.AreEqual(1, Volatile.Read(ref manifestCalls));
				Assert.AreEqual(2, holder.FileTable.Count);
				Assert.AreSame(baseEntry, holder.FileTable["data\\base.txt"]);
				Assert.AreSame(pendingEntry, holder.FileTable["data\\pending.txt"]);
				Assert.IsFalse(holder.FileTable.InternalContains("data\\must-not-merge.txt"));
				releaseManifest.Set();
				Assert.IsTrue(SpinWait.SpinUntil(() => first.Completed, 15000));
				Assert.IsTrue(first.Success, first.Error == null ? "save failed" : first.Error.ToString());
			}
		}

		[TestMethod]
		public void Async_start_failure_releases_save_reservation_for_next_save() {
			string path = CreateGrf("async-start-failure.grf", "data\\base.txt", new byte[] { 1 });

			using (var holder = new GrfHolder(path)) {
				holder.Commands.AddFile("data\\pending.txt", new byte[] { 2 });
				holder.Container.AsyncSaveStarter = action => { throw new InvalidOperationException("forced start failure"); };
				ContainerSaveResult failed = holder.Save(SyncMode.Asynchronous);

				Assert.IsFalse(failed.Success);
				Assert.IsTrue(failed.Completed);
				Assert.IsFalse(holder.IsBusy);
				ContainerSaveResult retry = holder.Save(SyncMode.Synchronous);
				Assert.IsTrue(retry.Success, retry.Error == null ? "retry failed" : retry.Error.ToString());
			}
		}

		[TestMethod]
		public void Buffered_source_cleanup_failure_is_warning_after_committed_success() {
			string destination = CreateGrf("buffer-cleanup-warning.grf", "data\\old.txt", new byte[] { 1 });
			FileEntry entry = CreateBufferedEntry("buffer-locked.bin", "data\\buffered.txt", new byte[] { 4, 5 });

			using (var locked = new FileStream(entry.SourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				SafeSaveOutcome outcome = GrfHolder.CreateFromBufferedFilesSafely(destination,
					new System.Collections.Generic.List<FileEntry> { entry }, new SafeSaveCoordinator(),
					(path, manifest) => new SafeGrfValidator().Validate(path, manifest), new SafeSaveOptions());

				Assert.IsTrue(outcome.Success, outcome.Report.ToString());
				Assert.IsTrue(outcome.Report.Items.Any(item => item.Code == "buffer.cleanup-failed" &&
					item.Severity == SafeSaveSeverity.Warning));
				Assert.IsTrue(File.Exists(entry.SourceFilePath));
			}

			using (var reopened = new GrfHolder(destination)) {
				CollectionAssert.AreEqual(new byte[] { 4, 5 }, reopened.FileTable["data\\buffered.txt"].GetDecompressedData());
			}
		}

		[TestMethod]
		public void Buffered_cleanup_never_deletes_backup_when_source_aliases_backup_path() {
			string destination = CreateGrf("buffer-protected-cleanup.grf", "data\\old.txt", new byte[] { 1 });
			byte[] originalHash = Hash(destination);
			FileEntry entry = CreateBufferedEntry("buffer-protected-cleanup.grf.bak", "data\\new.txt", new byte[] { 2 });
			entry.SourceFilePath = entry.SourceFilePath.ToUpperInvariant();

			GrfHolder.CreateFromBufferedFiles(destination, new System.Collections.Generic.List<FileEntry> { entry });

			Assert.IsTrue(File.Exists(destination + ".bak"));
			CollectionAssert.AreEqual(originalHash, Hash(destination + ".bak"));
		}

		[TestMethod]
		public void Manifest_entry_limit_is_checked_before_decompression_allocation() {
			string path = CreateGrf("manifest-bound.grf", "data\\entry.txt", new byte[] { 1, 2 });
			long previous = SafeSaveManifest.MaximumEntryBytes;
			try {
				SafeSaveManifest.MaximumEntryBytes = 1;
				using (var holder = new GrfHolder(path)) {
					try {
						SafeSaveManifest.Capture(holder);
						Assert.Fail("The entry limit must be checked before decompression.");
					}
					catch (SafeSaveEntryTooLargeException) { }
				}
			}
			finally {
				SafeSaveManifest.MaximumEntryBytes = previous;
			}
		}

		[TestMethod]
		public void CreateFromBufferedFiles_write_failure_releases_buffer_streams() {
			FileEntry entry = CreateBufferedEntry("buffer-write-failure.bin", "data\\buffered.txt", new byte[] { 3, 4 });
			string destination = Path.Combine(_temporaryDirectory, "missing", "buffered.grf");

			try {
				GrfHolder.CreateFromBufferedFiles(destination, new System.Collections.Generic.List<FileEntry> { entry });
				Assert.Fail("The write should fail when the destination directory does not exist.");
			}
			catch (DirectoryNotFoundException) {
			}

			using (FileStream stream = new FileStream(entry.SourceFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
				Assert.IsTrue(stream.Length > 0);
			}
		}

		private string CreateGrf(string fileName, string relativePath, byte[] data) {
			string path = Path.Combine(_temporaryDirectory, fileName);
			using (var holder = new GrfHolder()) {
				holder.New(path);
				holder.Commands.AddFile(relativePath, data);
				ContainerSaveResult result = holder.SaveAs(path);
				Assert.IsTrue(result.Success, result.Error == null ? "Initial save failed." : result.Error.ToString());
			}
			return path;
		}

		private void AssertEncryptionRollback(bool promotionFailure) {
			string path = CreateGrf(promotionFailure ? "encryption-promotion-rollback.grf" : "encryption-validation-rollback.grf",
				"data\\base.txt", new byte[] { 1, 2, 3 });
			byte[] originalHash = Hash(path);
			string encryptionSource = Path.Combine(Settings.TempPath, GrfStrings.EncryptionFilename);
			bool previousLockFiles = Settings.LockFiles;

			try {
				Settings.LockFiles = true;
				if (File.Exists(encryptionSource)) File.Delete(encryptionSource);
				using (var holder = new GrfHolder(path)) {
					FileEntry originalEntry = holder.FileTable["data\\base.txt"];
					EntryType originalFlags = originalEntry.Flags;
					Modification originalModification = originalEntry.Modification;
					string originalSource = originalEntry.SourceFilePath;
					holder.Header.IsEncrypted = true;
					holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(index => (byte)index).ToArray();
					if (promotionFailure) {
						holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());
					}
					else {
						holder.Container.SafeSaveValidator = (temporary, manifest) => {
							var report = new SafeSaveValidationReport();
							report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "Forced failure.");
							return report;
						};
					}

					ContainerSaveResult result = holder.Save();

					Assert.IsFalse(result.Success);
					Assert.IsFalse(holder.FileTable.InternalContains(GrfStrings.EncryptionFilename));
					Assert.AreEqual(1, holder.FileTable.Count);
					Assert.AreSame(originalEntry, holder.FileTable["data\\base.txt"]);
					Assert.AreEqual(originalFlags, originalEntry.Flags);
					Assert.AreEqual(originalModification, originalEntry.Modification);
					Assert.AreEqual(originalSource, originalEntry.SourceFilePath);
					using (var stream = new FileStream(encryptionSource, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
						Assert.AreEqual(4, stream.Length);
					}
					CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, originalEntry.GetDecompressedData());
				}
				CollectionAssert.AreEqual(originalHash, Hash(path));
			}
			finally {
				Settings.LockFiles = previousLockFiles;
				if (File.Exists(encryptionSource)) File.Delete(encryptionSource);
			}
		}

		private void AssertExternalEncryptionIndexWriteIsDeferred(bool promotionFailure) {
			string path = CreateGrf(promotionFailure ? "external-index-promotion.grf" : "external-index-validation.grf",
				"data\\base.txt", new byte[] { 1, 2, 3 });
			string index = Path.Combine(_temporaryDirectory, promotionFailure ? "promotion.index" : "validation.index");
			File.WriteAllBytes(index, new byte[] { 9, 8, 7, 6 });
			byte[] originalIndex = File.ReadAllBytes(index);

			using (var holder = new GrfHolder(path)) {
				holder.Header.IsEncrypted = true;
				holder.Header.EncryptionKey = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
				holder.Container.SafeSaveEncryptionIndexWriter = uid => File.WriteAllBytes(index, new byte[] { 0 });
				if (promotionFailure) {
					holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());
				}
				else {
					holder.Container.SafeSaveValidator = (temporary, manifest) => {
						var report = new SafeSaveValidationReport();
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "forced");
						return report;
					};
				}

				Assert.IsFalse(holder.Save().Success);
			}

			CollectionAssert.AreEqual(originalIndex, File.ReadAllBytes(index));
		}

		private void AssertMergeFailureRestoresPreCallTable(bool promotionFailure) {
			string destination = CreateGrf(promotionFailure ? "merge-promotion-failure.grf" : "merge-validation-failure.grf",
				"data\\base.txt", new byte[] { 1 });
			string addition = CreateGrf(promotionFailure ? "merge-promotion-addition.grf" : "merge-validation-addition.grf",
				"data\\added.txt", new byte[] { 2 });
			byte[] originalHash = Hash(destination);

			using (var holder = new GrfHolder(destination))
			using (var merge = new GrfHolder(addition)) {
				FileEntry baseEntry = holder.FileTable["data\\base.txt"];
				EntryType flags = baseEntry.Flags;
				Modification modification = baseEntry.Modification;
				if (promotionFailure) {
					holder.Container.SafeSaveCoordinator = new SafeSaveCoordinator(new IntactDestinationPromotionFailureFileSystem());
				}
				else {
					holder.Container.SafeSaveValidator = (temporary, manifest) => {
						var report = new SafeSaveValidationReport();
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "forced.validation", temporary, "Forced failure.");
						return report;
					};
				}

				ContainerSaveResult result = holder.Merge(merge);

				Assert.IsFalse(result.Success);
				Assert.AreEqual(1, holder.FileTable.Count);
				Assert.AreSame(baseEntry, holder.FileTable["data\\base.txt"]);
				Assert.AreEqual(flags, baseEntry.Flags);
				Assert.AreEqual(modification, baseEntry.Modification);
				Assert.IsFalse(holder.FileTable.InternalContains("data\\added.txt"));
			}

			CollectionAssert.AreEqual(originalHash, Hash(destination));
		}

		private static byte[] Hash(string path) {
			using (SHA256 sha256 = SHA256.Create())
			using (FileStream stream = File.OpenRead(path)) {
				return sha256.ComputeHash(stream);
			}
		}

		private FileEntry CreateBufferedEntry(string fileName, string relativePath, byte[] data) {
			byte[] compressed = Compression.CompressZlib(data);
			int alignment = (compressed.Length + 7) / 8 * 8;
			byte[] buffer = new byte[alignment];
			Buffer.BlockCopy(compressed, 0, buffer, 0, compressed.Length);
			string source = Path.Combine(_temporaryDirectory, fileName);
			File.WriteAllBytes(source, buffer);
			FileEntry entry = FileEntry.CreateBufferedEntry(source, relativePath, 0, compressed.Length, alignment, data.Length);
			entry.Flags = EntryType.File;
			return entry;
		}

		private static void AssertSafeSuccess(ContainerSaveResult result, SavingMode expectedMode) {
			Assert.IsTrue(result.Success, result.Error == null ? "Save failed." : result.Error.ToString());
			Assert.AreEqual(expectedMode, result.SaveModeUsed);
			Assert.IsNotNull(result.SafeSaveReport);
			Assert.IsFalse(result.SafeSaveReport.HasErrors, result.SafeSaveReport.ToString());
			Assert.IsFalse(string.IsNullOrEmpty(result.TemporaryFileName));
			Assert.IsFalse(File.Exists(result.TemporaryFileName));
		}

		private sealed class IntactDestinationPromotionFailureFileSystem : ISafeSaveFileSystem {
			private readonly SafeSaveFileSystem _inner = new SafeSaveFileSystem();

			public bool Exists(string path) => _inner.Exists(path);
			public long Length(string path) => _inner.Length(path);
			public long AvailableFreeSpace(string directory) => _inner.AvailableFreeSpace(directory);
			public void DeleteOwnedTemporary(string path) => _inner.DeleteOwnedTemporary(path);
			public void MoveNew(string temporaryPath, string destinationPath) => _inner.MoveNew(temporaryPath, destinationPath);

			public string ReplaceExisting(string temporaryPath, string destinationPath, string backupPath) {
				throw new SafeSavePromotionException("Forced promotion failure.", new IOException("Forced failure."), true,
					"promotion.destination-intact", destinationPath);
			}

			public SafeSaveReplaceResult ReplaceExistingGuarded(string temporaryPath, string destinationPath,
				string operationBackupPath, bool userBackupRequested) {
				throw new SafeSavePromotionException("Forced promotion failure.", new IOException("Forced failure."), true,
					"promotion.destination-intact", destinationPath);
			}

			public void CompleteGuardedReplace(SafeSaveReplaceResult replaceResult) { }

			public string RollbackConcurrentReplacement(string destinationPath, SafeSaveReplaceResult replaceResult) {
				throw new NotSupportedException();
			}
		}
	}
}
