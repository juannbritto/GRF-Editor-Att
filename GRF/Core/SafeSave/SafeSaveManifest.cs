using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;

namespace GRF.Core.SafeSave {
	public sealed class SafeSaveManifestEntry {
		private readonly byte[] _sha256;

		public SafeSaveManifestEntry(string relativePath, long sizeDecompressed, byte[] sha256) {
			if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));
			if (sha256 == null) throw new ArgumentNullException(nameof(sha256));

			RelativePath = relativePath;
			SizeDecompressed = sizeDecompressed;
			_sha256 = (byte[])sha256.Clone();
		}

		public string RelativePath { get; }
		public long SizeDecompressed { get; }
		public byte[] Sha256 => (byte[])_sha256.Clone();

		internal bool HashEquals(byte[] hash) {
			if (hash == null || hash.Length != _sha256.Length) return false;

			int difference = 0;
			for (int index = 0; index < hash.Length; index++) {
				difference |= hash[index] ^ _sha256[index];
			}

			return difference == 0;
		}
	}

	public sealed class SafeSaveManifest {
		private readonly Dictionary<string, SafeSaveManifestEntry> _entries;

		private SafeSaveManifest(Dictionary<string, SafeSaveManifestEntry> entries) {
			_entries = entries;
			Entries = new ReadOnlyDictionary<string, SafeSaveManifestEntry>(_entries);
		}

		public IReadOnlyDictionary<string, SafeSaveManifestEntry> Entries { get; }

		public static SafeSaveManifest Capture(GrfHolder holder) {
			if (holder == null) throw new ArgumentNullException(nameof(holder));
			return Capture(holder.Container);
		}

		internal static SafeSaveManifest Capture(Container container) {
			if (container == null) throw new ArgumentNullException(nameof(container));

			var entries = new Dictionary<string, SafeSaveManifestEntry>(StringComparer.OrdinalIgnoreCase);
			using (SHA256 sha256 = SHA256.Create()) {
				foreach (FileEntry entry in container.Table) {
					if ((entry.Modification & Modification.Removed) == Modification.Removed) continue;

					byte[] data = entry.GetDecompressedData();
					byte[] hash = sha256.ComputeHash(data);
					entries[entry.RelativePath] = new SafeSaveManifestEntry(entry.RelativePath, entry.GetSizeDecompressed(), hash);
					data = null;
				}
			}

			return new SafeSaveManifest(entries);
		}

		public void Compare(GrfHolder actual, SafeSaveValidationReport report) {
			if (actual == null) throw new ArgumentNullException(nameof(actual));
			if (report == null) throw new ArgumentNullException(nameof(report));

			var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (SHA256 sha256 = SHA256.Create()) {
				foreach (FileEntry entry in actual.FileTable) {
					if ((entry.Modification & Modification.Removed) == Modification.Removed) continue;

					SafeSaveManifestEntry expected;
					if (!_entries.TryGetValue(entry.RelativePath, out expected)) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.unexpected", entry.RelativePath, "Entry is not present in the expected manifest.");
						continue;
					}

					found.Add(entry.RelativePath);
					long size = entry.GetSizeDecompressed();
					if (size != expected.SizeDecompressed) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.size", entry.RelativePath, $"Expected {expected.SizeDecompressed} bytes but found {size} bytes.");
					}

					byte[] data = entry.GetDecompressedData();
					byte[] hash = sha256.ComputeHash(data);
					if (!expected.HashEquals(hash)) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.hash", entry.RelativePath, "Entry content hash differs from the expected manifest.");
					}
					data = null;
				}
			}

			foreach (KeyValuePair<string, SafeSaveManifestEntry> expected in _entries) {
				if (!found.Contains(expected.Key)) {
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.missing", expected.Value.RelativePath, "Expected entry is missing.");
				}
			}
		}
	}
}
