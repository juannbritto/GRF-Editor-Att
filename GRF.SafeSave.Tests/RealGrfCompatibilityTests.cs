using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GRF.Core;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class RealGrfCompatibilityTests {
		private string _disposableDirectory;

		[TestCleanup]
		public void TearDown() {
			if (_disposableDirectory != null && Directory.Exists(_disposableDirectory)) {
				Directory.Delete(_disposableDirectory, true);
			}
		}

		[TestMethod]
		[TestCategory("RealGrf")]
		public void Classic_fixture_round_trips_through_safe_save_with_exact_backup() {
			string fixtureDirectory = Environment.GetEnvironmentVariable("GRF_REAL_FIXTURE_DIR");
			if (string.IsNullOrWhiteSpace(fixtureDirectory) || !Directory.Exists(fixtureDirectory)) {
				Assert.Inconclusive("GRF_REAL_FIXTURE_DIR is not configured.");
			}

			string fixture = Path.Combine(Path.GetFullPath(fixtureDirectory), "classic-sample.grf");
			if (!File.Exists(fixture)) Assert.Inconclusive("The classic GRF fixture is missing.");
			_disposableDirectory = Path.Combine(Path.GetFullPath(fixtureDirectory), "test-runs", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_disposableDirectory);
			string working = Path.Combine(_disposableDirectory, "working.grf");
			File.Copy(fixture, working, false);
			string beforeHash = Sha256(working);
			SafeSaveManifest expected;

			using (var holder = new GrfHolder(working)) {
				holder.SafeSaveOptions = new SafeSaveOptions { CreateBackup = true, IncludeInformationItems = true };
				holder.Commands.AddFile("data\\grf_editor_safe_probe.txt", new byte[] { 0x47, 0x52, 0x46, 0x20, 0x53, 0x41, 0x46, 0x45 });
				expected = SafeSaveManifest.Capture(holder);
				var result = holder.Save();
				Assert.IsTrue(result.Success, result.SafeSaveReport == null ? result.Error?.ToString() : result.SafeSaveReport.ToString());
			}

			SafeSaveValidationReport report = new SafeGrfValidator().Validate(working, expected);
			Assert.IsFalse(report.HasErrors, report.ToString());
			Assert.IsTrue(File.Exists(working + ".bak"));
			Assert.AreEqual(beforeHash, Sha256(working + ".bak"));
			using (var reopened = new GrfHolder(working)) {
				Assert.IsNotNull(reopened.FileTable.TryGet("data\\grf_editor_safe_probe.txt"));
			}
		}

		private static string Sha256(string path) {
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (SHA256 hash = SHA256.Create()) {
				return string.Concat(hash.ComputeHash(stream).Select(value => value.ToString("x2")));
			}
		}
	}
}
