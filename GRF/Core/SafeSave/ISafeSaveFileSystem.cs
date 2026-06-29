namespace GRF.Core.SafeSave {
	internal interface ISafeSaveFileSystem {
		bool Exists(string path);
		long Length(string path);
		long AvailableFreeSpace(string directory);
		void DeleteOwnedTemporary(string path);
		void MoveNew(string temporaryPath, string destinationPath);
		string ReplaceExisting(string temporaryPath, string destinationPath, string backupPath);
	}
}
