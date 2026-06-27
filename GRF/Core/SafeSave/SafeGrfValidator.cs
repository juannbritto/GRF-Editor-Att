using System;
using System.IO;

namespace GRF.Core.SafeSave {
	public static class SafeGrfValidator {
		public static SafeSaveValidationReport Validate(string path, SafeSaveManifest expected) {
			var report = new SafeSaveValidationReport();

			try {
				if (!File.Exists(path) || new FileInfo(path).Length < GrfHeader.DataByteSize) {
					report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "header.invalid", path, "The GRF header is missing or truncated.");
					return report;
				}

				long fileLength = new FileInfo(path).Length;
				using (var holder = new GrfHolder(path)) {
					if (holder.WriteClassification.Capability != ContainerWriteCapability.Editable) {
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "format.not-editable", path, "The container format is not safely editable.");
					}

					long tableStart = holder.Header.FileTableOffset > long.MaxValue - GrfHeader.DataByteSize
						? long.MaxValue
						: holder.Header.FileTableOffset + GrfHeader.DataByteSize;

					foreach (FileEntry entry in holder.FileTable) {
						if (!HasValidBounds(entry, tableStart, fileLength)) {
							report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.bounds", entry.RelativePath, "Entry metadata points outside the GRF data region.");
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
