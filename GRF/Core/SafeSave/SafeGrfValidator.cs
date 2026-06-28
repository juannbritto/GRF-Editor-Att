using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GRF.Core.SafeSave {
	public sealed class SafeGrfValidator {
		public SafeSaveValidationReport Validate(string path, SafeSaveManifest expected) {
			var report = new SafeSaveValidationReport();

			try {
				if (!File.Exists(path) || new FileInfo(path).Length < GrfHeader.DataByteSize) {
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "header.invalid", path, "The GRF header is missing or truncated.");
					return report;
				}

				long fileLength = new FileInfo(path).Length;
				using (var holder = new GrfHolder(path)) {
					ContainerWriteClassification classification = holder.WriteClassification;
					if (classification.Capability != ContainerWriteCapability.Editable) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "format.not-editable", path, classification.ReasonCode);
					}

					long tableStart = holder.Header.FileTableOffset > long.MaxValue - GrfHeader.DataByteSize
						? long.MaxValue
						: holder.Header.FileTableOffset + GrfHeader.DataByteSize;
					HashSet<FileEntry> invalidEntries = ValidateLayout(holder.FileTable, tableStart, fileLength, report);
					AddDuplicatePathErrors(holder.FileTable.DuplicatePaths, report);

					foreach (FileEntry entry in holder.FileTable) {
						if (invalidEntries.Contains(entry)) {
							continue;
						}

						try {
							byte[] data = entry.GetDecompressedData();
							if (data.LongLength != entry.SizeDecompressed) {
								report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.size", entry.RelativePath, $"Metadata declares {entry.SizeDecompressed} bytes but decompression produced {data.LongLength} bytes.");
							}
						}
						catch (Exception exception) {
							report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "validation.exception", entry.RelativePath, exception.Message);
						}
					}

					if (expected != null) {
						expected.Compare(holder, report);
					}
				}
			}
			catch (Exception exception) {
				report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "validation.exception", path, exception.Message);
			}

			return report;
		}

		internal HashSet<FileEntry> ValidateLayout(IEnumerable<FileEntry> entries, long tableStart, long fileLength, SafeSaveValidationReport report) {
			var invalidEntries = new HashSet<FileEntry>();
			var validEntries = new List<FileEntry>();

			foreach (FileEntry entry in entries) {
				if (!HasValidBounds(entry, tableStart, fileLength)) {
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.bounds", entry.RelativePath, "Entry metadata points outside the GRF data region.");
					invalidEntries.Add(entry);
				}
				else {
					validEntries.Add(entry);
				}
			}

			List<FileEntry> ordered = validEntries
				.OrderBy(entry => entry.FileExactOffset)
				.ThenBy(entry => entry.SizeCompressedAlignment)
				.ToList();
			if (ordered.Count == 0) return invalidEntries;

			FileEntry active = ordered[0];
			for (int index = 1; index < ordered.Count; index++) {
				FileEntry current = ordered[index];
				if (current.FileExactOffset == active.FileExactOffset) {
					if (current.SizeCompressedAlignment != active.SizeCompressedAlignment) {
						AddOverlapError(current, report);
						if (current.SizeCompressedAlignment > active.SizeCompressedAlignment) active = current;
					}
					continue;
				}

				long distance = current.FileExactOffset - active.FileExactOffset;
				if (distance >= active.SizeCompressedAlignment) {
					active = current;
					continue;
				}

				if (current.SizeCompressedAlignment > 0) {
					AddOverlapError(current, report);
				}

				if (current.SizeCompressedAlignment > active.SizeCompressedAlignment - distance) {
					active = current;
				}
			}

			return invalidEntries;
		}

		internal void AddDuplicatePathErrors(IEnumerable<string> duplicatePaths, SafeSaveValidationReport report) {
			foreach (string duplicatePath in duplicatePaths) {
				report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.duplicate-path", duplicatePath, "The file table contains a duplicate entry path.");
			}
		}

		private static void AddOverlapError(FileEntry entry, SafeSaveValidationReport report) {
			report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.overlap", entry.RelativePath, "Entry data overlaps another non-identical entry interval.");
		}

		private static bool HasValidBounds(FileEntry entry, long tableStart, long fileLength) {
			long offset = entry.FileExactOffset;
			int compressed = entry.SizeCompressed;
			int alignment = entry.SizeCompressedAlignment;
			int decompressed = entry.SizeDecompressed;

			if (offset < GrfHeader.DataByteSize || compressed < 0 || alignment < compressed || decompressed < 0 || tableStart > fileLength) {
				return false;
			}

			return offset <= tableStart && alignment >= 0 && alignment <= tableStart - offset;
		}
	}
}
