using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;

namespace GRF.Core.SafeSave {
	public sealed class SafeSaveEntryTooLargeException : IOException {
		public SafeSaveEntryTooLargeException(string path, long length, long maximum)
			: base($"Entry '{path}' declares {length} bytes; safe validation limit is {maximum} bytes.") { }
	}

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
		private static long _maximumEntryBytes = 256L * 1024 * 1024;
		public static long MaximumEntryBytes {
			get => System.Threading.Interlocked.Read(ref _maximumEntryBytes);
			set => System.Threading.Interlocked.Exchange(ref _maximumEntryBytes, value);
		}
		private readonly Dictionary<string, SafeSaveManifestEntry> _entries;
		private readonly long _entryLimit;

		private SafeSaveManifest(Dictionary<string, SafeSaveManifestEntry> entries, long entryLimit) {
			_entries = entries;
			_entryLimit = entryLimit;
			Entries = new ReadOnlyDictionary<string, SafeSaveManifestEntry>(_entries);
		}

		public IReadOnlyDictionary<string, SafeSaveManifestEntry> Entries { get; }

		public static SafeSaveManifest Capture(GrfHolder holder) {
			if (holder == null) throw new ArgumentNullException(nameof(holder));
			return Capture(holder.Container);
		}

		internal static SafeSaveManifest Capture(Container container) {
			return Capture(container, entry => entry.RelativePath);
		}

		internal static SafeSaveManifest CaptureForRepackedSource(Container container) {
			return Capture(container, entry => entry.GetFixedFileName());
		}

		private static SafeSaveManifest Capture(Container container, Func<FileEntry, string> pathSelector) {
			if (container == null) throw new ArgumentNullException(nameof(container));

			var entries = new Dictionary<string, SafeSaveManifestEntry>(StringComparer.OrdinalIgnoreCase);
			long entryLimit = MaximumEntryBytes;
			using (SHA256 sha256 = SHA256.Create()) {
				foreach (FileEntry entry in EntriesIncludingEncryptionMetadata(container.InternalTable)) {
					if ((entry.Modification & Modification.Removed) == Modification.Removed &&
						(!string.Equals(entry.RelativePath, GrfStrings.EncryptionFilename, StringComparison.OrdinalIgnoreCase) ||
						 !container.InternalHeader.IsEncrypted)) continue;
					EnsureBounded(entry.RelativePath, entry.GetSizeDecompressed(), entryLimit);

					byte[] data = entry.GetDecompressedData();
					byte[] hash = sha256.ComputeHash(data);
					string relativePath = pathSelector(entry);
					entries[relativePath] = new SafeSaveManifestEntry(relativePath, entry.GetSizeDecompressed(), hash);
					data = null;
				}
			}

			return new SafeSaveManifest(entries, entryLimit);
		}

		internal static SafeSaveManifest CaptureBufferedEntries(IEnumerable<FileEntry> bufferedEntries) {
			if (bufferedEntries == null) throw new ArgumentNullException(nameof(bufferedEntries));

			var entries = new Dictionary<string, SafeSaveManifestEntry>(StringComparer.OrdinalIgnoreCase);
			long entryLimit = MaximumEntryBytes;
			using (SHA256 sha256 = SHA256.Create()) {
				foreach (FileEntry entry in bufferedEntries) {
					EnsureBounded(entry.RelativePath, entry.NewSizeCompressed, entryLimit);
					EnsureBounded(entry.RelativePath, entry.NewSizeDecompressed, entryLimit);
					byte[] compressed = new byte[entry.NewSizeCompressed];
					using (var stream = new FileStream(entry.SourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
						stream.Position = entry.TemporaryOffset;
						int offset = 0;
						while (offset < compressed.Length) {
							int read = stream.Read(compressed, offset, compressed.Length - offset);
							if (read == 0) throw new EndOfStreamException("A buffered GRF entry is truncated: " + entry.RelativePath);
							offset += read;
						}
					}

					byte[] data = Compression.Decompress(compressed, entry.NewSizeCompressed, entry.NewSizeDecompressed);
					entries[entry.RelativePath] = new SafeSaveManifestEntry(entry.RelativePath, data.LongLength, sha256.ComputeHash(data));
				}
			}

			return new SafeSaveManifest(entries, entryLimit);
		}

		public void Compare(GrfHolder actual, SafeSaveValidationReport report) {
			if (actual == null) throw new ArgumentNullException(nameof(actual));
			if (report == null) throw new ArgumentNullException(nameof(report));

			var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using (SHA256 sha256 = SHA256.Create()) {
				foreach (FileEntry entry in EntriesIncludingEncryptionMetadata(actual.FileTable)) {
					if ((entry.Modification & Modification.Removed) == Modification.Removed &&
						!string.Equals(entry.RelativePath, GrfStrings.EncryptionFilename, StringComparison.OrdinalIgnoreCase)) continue;

					SafeSaveManifestEntry expected;
					if (!_entries.TryGetValue(entry.RelativePath, out expected)) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.unexpected", entry.RelativePath, "Entry is not present in the expected manifest.");
						continue;
					}

					found.Add(entry.RelativePath);
					long size = entry.GetSizeDecompressed();
					EnsureBounded(entry.RelativePath, size, _entryLimit);
					if (size != expected.SizeDecompressed) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.size", entry.RelativePath, $"Expected {expected.SizeDecompressed} bytes but found {size} bytes.");
					}

					try {
						byte[] data = entry.GetDecompressedData();
						byte[] hash = sha256.ComputeHash(data);
						if (!expected.HashEquals(hash)) {
							report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.hash", entry.RelativePath, "Entry content hash differs from the expected manifest.");
						}
					}
					catch (SafeSaveEntryTooLargeException exception) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.entry-too-large", entry.RelativePath, exception.Message);
					}
					catch (Exception exception) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "validation.exception", entry.RelativePath, exception.Message);
					}
				}
			}

			foreach (KeyValuePair<string, SafeSaveManifestEntry> expected in _entries) {
				if (!found.Contains(expected.Key)) {
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.missing", expected.Value.RelativePath, "Expected entry is missing.");
				}
			}
		}

		internal static void EnsureBounded(string path, long length) {
			EnsureBounded(path, length, MaximumEntryBytes);
		}

		private static void EnsureBounded(string path, long length, long maximum) {
			if (maximum <= 0 || length < 0 || length > maximum)
				throw new SafeSaveEntryTooLargeException(path, length, maximum);
		}

		internal static IEnumerable<FileEntry> EntriesIncludingEncryptionMetadata(FileTable table) {
			foreach (FileEntry entry in table) yield return entry;
			if (table.InternalContains(GrfStrings.EncryptionFilename)) {
				FileEntry encryptionEntry = table[GrfStrings.EncryptionFilename];
				if ((encryptionEntry.Modification & Modification.Removed) == Modification.Removed) yield return encryptionEntry;
			}
		}
	}
}
