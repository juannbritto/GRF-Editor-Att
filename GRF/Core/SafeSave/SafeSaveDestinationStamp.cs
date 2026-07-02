using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GRF.Core.SafeSave {
	/// <summary>Immutable observation of the exact destination object approved during preflight.</summary>
	internal sealed class SafeSaveDestinationStamp {
		internal SafeSaveDestinationStamp(long length, long lastWriteUtcTicks, byte[] header,
			bool hasFileIdentity, uint volumeSerialNumber, uint fileIndexHigh, uint fileIndexLow,
			ContainerWriteCapability capability = ContainerWriteCapability.Editable, string reasonCode = "classic-supported") {
			Length = length;
			LastWriteUtcTicks = lastWriteUtcTicks;
			Header = header == null ? Array.Empty<byte>() : (byte[])header.Clone();
			HasFileIdentity = hasFileIdentity;
			VolumeSerialNumber = volumeSerialNumber;
			FileIndexHigh = fileIndexHigh;
			FileIndexLow = fileIndexLow;
			Capability = capability;
			ReasonCode = reasonCode;
		}

		internal long Length { get; }
		internal long LastWriteUtcTicks { get; }
		internal byte[] Header { get; }
		internal bool HasFileIdentity { get; }
		internal uint VolumeSerialNumber { get; }
		internal uint FileIndexHigh { get; }
		internal uint FileIndexLow { get; }
		internal ContainerWriteCapability Capability { get; }
		internal string ReasonCode { get; }
		internal bool IsEditable => Capability == ContainerWriteCapability.Editable;

		internal bool Matches(SafeSaveDestinationStamp other) {
			if (other == null || Length != other.Length || LastWriteUtcTicks != other.LastWriteUtcTicks ||
				!Header.SequenceEqual(other.Header) || Capability != other.Capability ||
				!string.Equals(ReasonCode, other.ReasonCode, StringComparison.Ordinal)) return false;
			if (HasFileIdentity != other.HasFileIdentity) return false;
			return !HasFileIdentity || VolumeSerialNumber == other.VolumeSerialNumber &&
				FileIndexHigh == other.FileIndexHigh && FileIndexLow == other.FileIndexLow;
		}

		internal static SafeSaveDestinationStamp CaptureGrf(string path) {
			byte[] header = new byte[GrfHeader.DataByteSize];
			long length;
			long lastWrite = 0;
			bool hasIdentity = false;
			uint volume = 0, high = 0, low = 0;
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
				FileShare.ReadWrite | FileShare.Delete)) {
				length = stream.Length;
				int offset = 0;
				while (offset < header.Length) {
					int read = stream.Read(header, offset, header.Length - offset);
					if (read == 0) break;
					offset += read;
				}
				if (offset != header.Length) Array.Resize(ref header, offset);
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					ByHandleFileInformation info;
					if (GetFileInformationByHandle(stream.SafeFileHandle, out info)) {
						hasIdentity = true;
						volume = info.VolumeSerialNumber;
						high = info.FileIndexHigh;
						low = info.FileIndexLow;
						lastWrite = ((long)info.LastWriteTime.dwHighDateTime << 32) |
							(uint)info.LastWriteTime.dwLowDateTime;
					}
				}
			}
			if (!hasIdentity) lastWrite = File.GetLastWriteTimeUtc(path).ToFileTimeUtc();
			ContainerWriteClassification classification = ClassifyHeader(header);
			return new SafeSaveDestinationStamp(length, lastWrite, header, hasIdentity, volume, high, low,
				classification.Capability, classification.ReasonCode);
		}

		private static ContainerWriteClassification ClassifyHeader(byte[] header) {
			if (header.Length < GrfHeader.DataByteSize) {
				return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyUnknown, "header-errors");
			}
			string magic = Encoding.ASCII.GetString(header, 0, 16);
			int version = BitConverter.ToInt32(header, 42);
			return ContainerWritePolicy.Classify(magic, (byte)(version >> 8), (byte)version, false);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct ByHandleFileInformation {
			internal uint FileAttributes;
			internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
			internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
			internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
			internal uint VolumeSerialNumber;
			internal uint FileSizeHigh;
			internal uint FileSizeLow;
			internal uint NumberOfLinks;
			internal uint FileIndexHigh;
			internal uint FileIndexLow;
		}

		[DllImport("kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetFileInformationByHandle(SafeFileHandle fileHandle,
			out ByHandleFileInformation fileInformation);
	}
}
