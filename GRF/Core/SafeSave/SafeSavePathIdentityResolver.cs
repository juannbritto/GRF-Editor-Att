using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GRF.Core.SafeSave {
	internal interface ISafeSavePathIdentityResolver {
		string Resolve(string path);
	}

	internal sealed class SafeSavePathIdentityResolver : ISafeSavePathIdentityResolver {
		private const uint FileReadAttributes = 0x80;
		private const uint FileShareRead = 0x1;
		private const uint FileShareWrite = 0x2;
		private const uint FileShareDelete = 0x4;
		private const uint OpenExisting = 3;
		private const uint FileFlagBackupSemantics = 0x02000000;

		public string Resolve(string path) {
			string fullPath = Path.GetFullPath(path);
			var missingSegments = new Stack<string>();
			string existingPath = fullPath;

			while (!File.Exists(existingPath) && !Directory.Exists(existingPath)) {
				string name = Path.GetFileName(existingPath);
				string parent = Path.GetDirectoryName(existingPath);
				if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) break;
				missingSegments.Push(name);
				existingPath = parent;
			}

			string identity = TryResolveExisting(existingPath) ?? existingPath;
			while (missingSegments.Count > 0) identity = Path.Combine(identity, missingSegments.Pop());
			string normalized = Path.GetFullPath(identity);
			string root = Path.GetPathRoot(normalized);
			return normalized.Length > root.Length
				? normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
				: normalized;
		}

		private static string TryResolveExisting(string path) {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
			try {
				using (SafeFileHandle handle = CreateFile(path, FileReadAttributes,
					FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, OpenExisting,
					FileFlagBackupSemantics, IntPtr.Zero)) {
					if (handle.IsInvalid) return null;
					var buffer = new StringBuilder(512);
					uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
					if (length == 0) return null;
					if (length >= buffer.Capacity) {
						buffer.Capacity = checked((int)length + 1);
						length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
						if (length == 0 || length >= buffer.Capacity) return null;
					}
					return RemoveDevicePrefix(buffer.ToString());
				}
			}
			catch (IOException) {
				return null;
			}
			catch (UnauthorizedAccessException) {
				return null;
			}
		}

		private static string RemoveDevicePrefix(string path) {
			const string uncPrefix = @"\\?\UNC\";
			const string devicePrefix = @"\\?\";
			if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(uncPrefix.Length);
			if (path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)) return path.Substring(devicePrefix.Length);
			return path;
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
			IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder filePath,
			uint filePathLength, uint flags);
	}
}
