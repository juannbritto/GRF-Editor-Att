using System;
using System.IO;
using System.Linq;
using GRF.Core;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class SafeGrfValidatorTests {
		private string _temporaryDirectory;

		[TestInitialize]
		public void SetUp() {
			_temporaryDirectory = Path.Combine(Path.GetTempPath(), "GRF.SafeSave.Tests", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_temporaryDirectory);
		}

		[TestCleanup]
		public void TearDown() {
			if (Directory.Exists(_temporaryDirectory)) {
				Directory.Delete(_temporaryDirectory, true);
			}
		}

		[TestMethod]
		public void Valid_grf_matches_expected_logical_manifest() {
			string path = CreateGrf("valid.grf", "data\\hello.txt", new byte[] { 1, 2, 3 });
			SafeSaveManifest expected;

			using (var reopened = new GrfHolder(path)) {
				expected = SafeSaveManifest.Capture(reopened);
			}

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(path, expected);

			Assert.IsFalse(report.HasErrors, report.ToString());
		}

		[TestMethod]
		public void Truncated_grf_reports_structural_error() {
			string path = Path.Combine(_temporaryDirectory, "truncated.grf");
			File.WriteAllBytes(path, new byte[20]);

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(path, null);

			Assert.IsTrue(report.HasErrors);
			Assert.IsTrue(report.Items.Any(item => item.Code == "header.invalid"));
		}

		[TestMethod]
		public void Protected_format_report_includes_reason_code() {
			string path = CreateEmptyEventHorizon("protected.grf");

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(path, null);
			SafeSaveValidationItem item = report.Items.Single(candidate => candidate.Code == "format.not-editable");

			Assert.AreEqual("event-horizon", item.Message);
		}

		[TestMethod]
		public void Existing_validation_error_skips_manifest_comparison() {
			string expectedPath = CreateGrf("expected-classic.grf", "data\\expected.txt", new byte[] { 1 });
			string protectedPath = CreateEmptyEventHorizon("protected-with-manifest.grf");
			SafeSaveManifest expected;

			using (var expectedHolder = new GrfHolder(expectedPath)) {
				expected = SafeSaveManifest.Capture(expectedHolder);
			}

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(protectedPath, expected);

			Assert.IsTrue(report.Items.Any(item => item.Code == "format.not-editable"));
			Assert.IsFalse(report.Items.Any(item => item.Code.StartsWith("manifest.", StringComparison.Ordinal)), report.ToString());
		}

		[TestMethod]
		public void Manifest_compare_reports_hash_mismatch() {
			string expectedPath = CreateGrf("expected.grf", "data\\same.txt", new byte[] { 1, 2, 3 });
			string actualPath = CreateGrf("actual.grf", "data\\same.txt", new byte[] { 3, 2, 1 });
			SafeSaveManifest expected;

			using (var expectedHolder = new GrfHolder(expectedPath)) {
				expected = SafeSaveManifest.Capture(expectedHolder);
			}

			SafeSaveValidationReport report;
			using (var actualHolder = new GrfHolder(actualPath)) {
				report = new SafeSaveValidationReport();
				expected.Compare(actualHolder, report);
			}

			Assert.IsTrue(report.Items.Any(item => item.Code == "manifest.hash"));
		}

		[TestMethod]
		public void Manifest_compare_reports_missing_and_unexpected_entries() {
			string expectedPath = CreateGrf("missing-expected.grf", "data\\expected.txt", new byte[] { 1 });
			string actualPath = CreateGrf("unexpected-actual.grf", "data\\actual.txt", new byte[] { 1 });
			SafeSaveManifest expected;

			using (var expectedHolder = new GrfHolder(expectedPath)) {
				expected = SafeSaveManifest.Capture(expectedHolder);
			}

			SafeSaveValidationReport report;
			using (var actualHolder = new GrfHolder(actualPath)) {
				report = new SafeSaveValidationReport();
				expected.Compare(actualHolder, report);
			}

			Assert.IsTrue(report.Items.Any(item => item.Code == "manifest.missing"));
			Assert.IsTrue(report.Items.Any(item => item.Code == "manifest.unexpected"));
		}

		[TestMethod]
		public void Layout_reports_partially_overlapping_entries() {
			var report = new SafeSaveValidationReport();
			var entries = new[] {
				CreateEntry("data\\first.txt", 50, 20),
				CreateEntry("data\\second.txt", 60, 10)
			};

			new SafeGrfValidator().ValidateLayout(entries, 100, 100, report);

			Assert.IsTrue(report.Items.Any(item => item.Code == "entry.overlap"));
		}

		[TestMethod]
		public void Layout_allows_identical_redirected_intervals() {
			var report = new SafeSaveValidationReport();
			var entries = new[] {
				CreateEntry("data\\first.txt", 50, 20),
				CreateEntry("data\\redirect.txt", 50, 20)
			};

			new SafeGrfValidator().ValidateLayout(entries, 100, 100, report);

			Assert.IsFalse(report.Items.Any(item => item.Code == "entry.overlap"));
		}

		[TestMethod]
		public void Layout_rejects_same_start_with_different_lengths() {
			var report = new SafeSaveValidationReport();
			var entries = new[] {
				CreateEntry("data\\short.txt", 50, 10),
				CreateEntry("data\\long.txt", 50, 20)
			};

			new SafeGrfValidator().ValidateLayout(entries, 100, 100, report);

			Assert.IsTrue(report.Items.Any(item => item.Code == "entry.overlap"));
		}

		[TestMethod]
		public void Layout_reports_out_of_bounds_entry() {
			var report = new SafeSaveValidationReport();

			new SafeGrfValidator().ValidateLayout(new[] { CreateEntry("data\\bad.txt", 45, 1) }, 100, 100, report);

			Assert.IsTrue(report.Items.Any(item => item.Code == "entry.bounds"));
		}

		[TestMethod]
		public void File_table_records_non_correction_duplicate_paths() {
			var table = new FileTable(null);
			table.RegisterLoadedEntry(CreateEntry("data\\same.txt", 50, 10));
			table.RegisterLoadedEntry(CreateEntry("DATA\\SAME.TXT", 70, 10));

			Assert.AreEqual(1, table.DuplicatePaths.Count);
			Assert.AreEqual("DATA\\SAME.TXT", table.DuplicatePaths[0]);
		}

		[TestMethod]
		public void File_table_does_not_record_intentional_filename_correction() {
			var table = new FileTable(null);
			FileEntry corrected = CreateEntry("data\\same.txt", 50, 10);
			corrected.Modification = Modification.FileNameRenamed;
			table.RegisterLoadedEntry(corrected);

			table.RegisterLoadedEntry(CreateEntry("data\\same.txt", 70, 10));

			Assert.AreEqual(0, table.DuplicatePaths.Count);
		}

		[TestMethod]
		public void Validator_reports_each_duplicate_path() {
			var report = new SafeSaveValidationReport();

			new SafeGrfValidator().AddDuplicatePathErrors(new[] { "data\\a.txt", "data\\b.txt" }, report);

			Assert.AreEqual(2, report.Items.Count(item => item.Code == "entry.duplicate-path"));
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

		private string CreateEmptyEventHorizon(string fileName) {
			string path = Path.Combine(_temporaryDirectory, fileName);

			using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			using (var writer = new BinaryWriter(stream)) {
				writer.Write(System.Text.Encoding.ASCII.GetBytes(GrfStrings.EventHorizon));
				writer.Write(new byte[14]);
				writer.Write((long)0);
				writer.Write(0);
				writer.Write(0x300);
				writer.Write(0);
				writer.Write(0);
				writer.Write(0);
			}

			return path;
		}

		private static FileEntry CreateEntry(string path, long offset, int alignment) {
			return new FileEntry {
				RelativePath = path,
				FileExactOffset = offset,
				SizeCompressed = alignment,
				SizeCompressedAlignment = alignment,
				SizeDecompressed = 1
			};
		}
	}
}
