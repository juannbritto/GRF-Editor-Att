using System;
using System.IO;
using System.Text;

namespace GRF.Core.SafeSave {
	public static class ContainerWritePolicy {
		public static ContainerWriteClassification Classify(string magic, byte major, byte minor, bool foundErrors) {
			if (magic == GrfStrings.EventHorizon) {
				return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyProtected, "event-horizon");
			}

			if (magic == GrfStrings.MasterOfMagic && !foundErrors) {
				return IsSupportedClassicVersion(major, minor)
					? new ContainerWriteClassification(ContainerWriteCapability.Editable, "classic-supported")
					: new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyUnknown, "unsupported-version");
			}

			return new ContainerWriteClassification(
				ContainerWriteCapability.ReadOnlyUnknown,
				foundErrors ? "header-errors" : "unknown-format");
		}

		public static ContainerWriteClassification Classify(GrfHeader header) {
			return Classify(header.Magic, header.MajorVersion, header.MinorVersion, header.FoundErrors);
		}

		internal static ContainerWriteClassification ClassifyFileHeader(string path) {
			try {
				byte[] header = new byte[GrfHeader.DataByteSize];
				using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
					int offset = 0;
					while (offset < header.Length) {
						int read = stream.Read(header, offset, header.Length - offset);
						if (read == 0) return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyUnknown, "header-errors");
						offset += read;
					}
				}

				string magic = Encoding.ASCII.GetString(header, 0, 16);
				int version = BitConverter.ToInt32(header, 42);
				return Classify(magic, (byte)(version >> 8), (byte)version, false);
			}
			catch (IOException) {
				throw;
			}
			catch (UnauthorizedAccessException) {
				throw;
			}
			catch {
				return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyUnknown, "header-errors");
			}
		}

		private static bool IsSupportedClassicVersion(byte major, byte minor) {
			return major == 1 && (minor == 2 || minor == 3) || major == 2 && minor == 0;
		}
	}
}
