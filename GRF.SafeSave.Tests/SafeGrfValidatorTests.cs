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

			SafeSaveValidationReport report = SafeGrfValidator.Validate(path, expected);

			Assert.IsFalse(report.HasErrors, report.ToString());
		}

		[TestMethod]
		public void Truncated_grf_reports_structural_error() {
			string path = Path.Combine(_temporaryDirectory, "truncated.grf");
			File.WriteAllBytes(path, new byte[20]);

			SafeSaveValidationReport report = SafeGrfValidator.Validate(path, null);

			Assert.IsTrue(report.HasErrors);
			Assert.IsTrue(report.Items.Any(item => item.Code == "header.invalid"));
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
