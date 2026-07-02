using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ErrorManager;
using GRF.ContainerFormat;
using GRF.FileFormats;
using GRF.FileFormats.ThorFormat;
using GRF.IO;
using GRF.GrfSystem;
using GRF.Threading;
using Utilities;
using Utilities.Extension;
using GRF.Core.GrfWriters;
using GRF.Core.SafeSave;

namespace GRF.Core {
	internal class Container : ContainerAbstract<FileEntry>, IDisposable {
		private TkDictionary<string, object> _attached = new TkDictionary<string, object>();

		internal Container() {
			_header = new GrfHeader(this);
			_table = new FileTable(InternalHeader);
			FileName = "new.grf";
			IsBusy = false;
			IsNewGrf = true;
			State = ContainerState.Normal;
		}

		internal Container(string fileName, GrfLoadData loadData = null) {
			try {
				GrfExceptions.IfNullThrow(fileName, "fileName");
				_load(fileName, loadData);
			}
			catch (Exception err) {
				if (_header == null) {
					_header = new GrfHeader(this);
					_header.SetError("Null header, this object was forcibly instantiated.");
				}

				_header.SetError(err.Message);
			}
			finally {
				AProgress.Finalize(this);
			}
		}

		internal ByteReaderStream Reader {
			get { return _reader ?? (_reader = new ByteReaderStream()); }
			set { _reader = value; }
		}

		private FileTable _table;
		private GrfHeader _header;

		/// <summary>
		/// Gets or sets the table.
		/// </summary>
		public override ContainerTable<FileEntry> Table {
			get { return _table; }
			protected set { _table = (FileTable) value; }
		}

		/// <summary>
		/// Gets or sets the table.
		/// </summary>
		public FileTable InternalTable {
			get { return _table; }
			set { _table = value; }
		}

		/// <summary>
		/// Gets the container header.
		/// </summary>
		public override FileHeader Header {
			get { return _header; }
			internal set { _header = (GrfHeader) value; }
		}

		/// <summary>
		/// Gets the container header.
		/// </summary>
		public GrfHeader InternalHeader {
			get { return _header; }
			internal set { _header = value; }
		}

		/// <summary>
		/// Gets a value indicating whether this container is modified (from the Commands object).
		/// </summary>
		public bool IsModified => Commands.IsModified;

		/// <summary>
		/// Gets or sets the name of the file.
		/// </summary>
		public string FileName { get; internal set; }

		/// <summary>
		/// Gets or sets a value indicating whether this container is a new GRF (doesn't have a source file yet).
		/// </summary>
		public bool IsNewGrf { get; set; }

		/// <summary>
		/// Gets or sets attached properties.
		/// </summary>
		internal TkDictionary<string, object> Attached {
			get { return _attached; }
			set { _attached = value; }
		}

		internal override string UniqueString {
			get {
				if (IsNewGrf)
					return "" + (FileName ?? "null").GetHashCode();

				return base.UniqueString;
			}
		}

		/// <summary>
		/// Gets an attached value and converts it to the requested format.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="key">The key.</param>
		/// <returns>The attached properties</returns>
		internal T GetAttachedProperty<T>(string key) {
			object value;

			if (!_attached.TryGetValue(key, out value)) {
				value = default(T);
				_attached[key] = value;
			}

			return (T)value;
		}

		/// <summary>
		/// Converts the current container to a GRF container.
		/// </summary>
		/// <param name="grfName">Name of the GRF.</param>
		/// <returns>The converted container to a GRF container.</returns>
		internal override Container ToGrfContainer(string grfName = null) {
			throw new InvalidOperationException();
		}

		private void _load(string fileName, GrfLoadData loadData = null) {
			try {
				if (fileName == null)
					return;

				IsNewGrf = false;
				if (Volatile.Read(ref _saveReservation) == 0) IsBusy = false;
				FileName = fileName;
				Reader = new ByteReaderStream(FileName);
				InternalHeader = new GrfHeader(Reader, this);
				Reader.PositionLong = InternalHeader.FileTableOffset + GrfHeader.DataByteSize;

				if (loadData != null && loadData.DecryptFileTable && loadData.EncryptionKey != null) {
					InternalHeader.DecryptFileTable = true;
					InternalHeader.EncryptionKey = loadData.EncryptionKey;
				}

				_table = new FileTable(InternalHeader, Reader);

				var encryptedCheckEntry = Table.TryGet(GrfStrings.EncryptionFilename);

				if (encryptedCheckEntry != null) {
					InternalHeader.IsEncrypted = true;
					Debug.Ignore(() => InternalHeader.EncryptionHashValue = BitConverter.ToUInt32(encryptedCheckEntry.GetDecompressedData(), 0));
				}
			}
			catch (Exception err) {
				if (Header != null) {
					_header.SetError(GrfStrings.FailedReadContainer, err.Message);
				}
				else
					ErrorHandler.HandleException(GrfExceptions.__CouldNotLoadGrf, err, ErrorLevel.Warning);
			}
			finally {
				Progress = 100f;
			}
		}

		public byte[] GetRawData(FileEntry node) {
			return node.GetCompressedData();
		}

		public byte[] GetDecompressedData(FileEntry node) {
			return node.GetDecompressedData();
		}

		protected override void _init() {
			throw new InvalidOperationException();
		}

		protected override void _onPreviewDispose() {
			Reader?.Close();
		}

		public ContainerSaveResult SaveResult;
		internal SafeSaveOptions SafeSaveOptions { get; set; } = new SafeSaveOptions();
		internal Func<string, SafeSaveManifest, SafeSaveValidationReport> SafeSaveValidator { get; set; } =
			(path, manifest) => new SafeGrfValidator().Validate(path, manifest);
		internal SafeSaveCoordinator SafeSaveCoordinator { get; set; } = new SafeSaveCoordinator();
		internal Action<string, SavingMode, bool> SafeSaveReload { get; set; }
		internal Action<string> SafeSaveEncryptionIndexWriter { get; set; }
		internal Func<Container, SafeSaveManifest> SafeSaveManifestFactory { get; set; } =
			container => SafeSaveManifest.Capture(container);
		internal Action<Action> AsyncSaveStarter { get; set; } = action => GrfThread.Start(action);
		internal Func<string, SafeSaveValidationReport> SafeThorValidator { get; set; } = ValidateThorArchive;
		private int _saveReservation;
		private int _saveOwnerThreadId;
		[ThreadStatic] private static Container _mutationAuthorizedContainer;
		[ThreadStatic] private static int _mutationAuthorizationDepth;
		internal bool IsCurrentSaveThread => Volatile.Read(ref _saveReservation) != 0 &&
			Volatile.Read(ref _saveOwnerThreadId) == Thread.CurrentThread.ManagedThreadId;
		internal bool IsInternalMutationAuthorized => ReferenceEquals(_mutationAuthorizedContainer, this) &&
			_mutationAuthorizationDepth > 0;
		internal IDisposable AuthorizeInternalMutation() {
			if (_mutationAuthorizationDepth == 0) _mutationAuthorizedContainer = this;
			if (!ReferenceEquals(_mutationAuthorizedContainer, this)) throw new InvalidOperationException("Nested mutation scope belongs to another container.");
			_mutationAuthorizationDepth++;
			return new InternalMutationScope(this);
		}
		private sealed class InternalMutationScope : IDisposable {
			private Container _container;
			internal InternalMutationScope(Container container) { _container = container; }
			public void Dispose() {
				if (_container == null) return;
				_mutationAuthorizationDepth--;
				if (_mutationAuthorizationDepth == 0) _mutationAuthorizedContainer = null;
				_container = null;
			}
		}

		/// <summary>
		/// Saves the container to the hard drive.
		/// </summary>
		/// <param name="fileName">Name of the file (null if overwriting the current container).</param>
		/// <param name="mergeGrf">The GRF to merge (null if nothing to merge).</param>
		/// <param name="mode">The saving mode.</param>
		/// <param name="syncMode">The synchronize mode (default to Synchronous.</param>
		public ContainerSaveResult Save(string fileName, Container mergeGrf, SavingMode mode, SyncMode syncMode) {
			ContainerSaveResult result = new ContainerSaveResult(this, fileName, mergeGrf, mode, syncMode);
			if (Interlocked.CompareExchange(ref _saveReservation, 1, 0) != 0) {
				result.Fail(GrfExceptions.__ContainerSaving.Create());
				result.Completed = true;
				return result;
			}

			if (IsBusy) {
				Interlocked.Exchange(ref _saveReservation, 0);
				result.Fail(GrfExceptions.__ContainerSaving.Create());
				result.Completed = true;
				return result;
			}

			IsBusy = true;
			SaveResult = result;
			Action operation = () => {
				Volatile.Write(ref _saveOwnerThreadId, Thread.CurrentThread.ManagedThreadId);
				try {
					_executeReservedSave(fileName, mergeGrf, mode, result);
				}
				finally {
					result.Completed = true;
					SaveResult = result;
					_releaseSaveReservationLast();
				}
			};

			if (syncMode == SyncMode.Asynchronous) {
				try {
					AsyncSaveStarter(operation);
				}
				catch (Exception exception) {
					if (!result.Completed) {
						result.Fail(exception);
						result.Completed = true;
						SaveResult = result;
						_releaseSaveReservationLast();
					}
				}
				return result;
			}

			operation();
			return result;
		}

		private void _releaseSaveReservationLast() {
			Volatile.Write(ref _saveOwnerThreadId, 0);
			IsBusy = false;
			Interlocked.Exchange(ref _saveReservation, 0);
		}

		private void _executeReservedSave(string fileName, Container mergeGrf, SavingMode mode,
			ContainerSaveResult result) {
			SafeSaveManifest safeSaveManifest = null;
			bool mergeAlreadyApplied = false;
			ContainerWriteStateSnapshot preOperationState = null;

			try {
				string ext = (fileName ?? FileName).GetExtension();
				if (ext == ".grf" || ext == ".gpf") {
					ContainerWriteClassification classification = ContainerWritePolicy.Classify(InternalHeader);
					if (!classification.CanWrite) {
						result.SafeSaveReport = new SafeSaveValidationReport();
						result.SafeSaveReport.Add(SafeSavePhase.Preflight, SafeSaveSeverity.Error,
							"format.not-editable", fileName ?? FileName, classification.ReasonCode);
						throw new SafeSaveFormatReadOnlyException(classification);
					}
					if (mode == SavingMode.FileEdit) mode = SavingMode.FileCopy;
				}
				GrfExceptions.IfEncryptionCheckFlagThrow(this);

				switch (mode) {
					case SavingMode.FileCopy:
					case SavingMode.FileEdit:
						if (mergeGrf != null && mergeGrf.IsModified)
							throw GrfExceptions.__AddedGrfModified.Create();
						break;
					case SavingMode.RepackSource:
						if (ext != ".thor" && ext != ".grf" && ext != ".gpf")
							throw GrfExceptions.__OperationNotAllowed.Create();
						break;
					case SavingMode.Repack:
						if (IsModified || IsNewGrf)
							throw GrfExceptions.__ContainerNotSavedForRepack.Create();

						switch (ext) {
							case ".thor":
								mode = SavingMode.Thor;
								InternalHeader.ThorSettings.Repack = true;
								break;
							case ".rgz":
								throw GrfExceptions.__InvalidContainerFormat.Create(fileName ?? FileName, ".grf, .gpf or .thor");
						}
						break;
					case SavingMode.Compact:
						if (IsModified || IsNewGrf)
							throw GrfExceptions.__ContainerNotSavedForCompact.Create();

						switch (ext) {
							case ".thor":
								mode = SavingMode.Thor;
								break;
							case ".rgz":
								mode = SavingMode.Rgz;
								break;
						}
						break;
				}
				result.SaveModeUsed = mode;

				if ((((ext == ".grf" || ext == ".gpf") &&
					 (mode == SavingMode.FileCopy || mode == SavingMode.Repack || mode == SavingMode.Compact)) ||
					mode == SavingMode.RepackSource) || mode == SavingMode.Thor) {
					preOperationState = ContainerWriteStateSnapshot.Capture(this);
					if (mergeGrf != null) {
						_applyMergeOnTable(mergeGrf);
						mergeAlreadyApplied = true;
					}
					if (mode != SavingMode.Thor) {
						WriterHelper.EncryptionCheck(this);
						safeSaveManifest = SafeSaveManifestFactory(this);
					}
				}

				_save(fileName, mergeGrf, mode, result, safeSaveManifest, mergeAlreadyApplied, preOperationState);
			}
			catch (Exception err) {
				_restoreState(preOperationState, result, restoreReader: false, promotionError: null);
				result.Fail(err);
			}

		}

		private void _internalSave(string fileName, Container mergeGrf, SavingMode mode, ContainerSaveResult result,
			bool mergeAlreadyApplied = false, bool deferExternalEncryptionIndex = false) {
			bool shouldRepack = false;

			try {
				// Validation before saving
				if (fileName.GetExtension() == null && mode != SavingMode.FileEdit)
					throw new InvalidOperationException("The file name must end with : .grf | .gpf | .rgz");
				if (_headerCheckFailed()) {
					result.Cancelled();
					return;
				}

				_headerEncryptedFailed(fileName);

				try {
					bool exists = File.Exists(fileName);
					_table.ResetTemporaryOffsets();

					// Merge GRF validation
					switch (mode) {
						case SavingMode.FileCopy:
							if (!mergeAlreadyApplied) _applyMergeOnTable(mergeGrf);
							break;
						case SavingMode.FileEdit:
							_applyMergeOnTable(mergeGrf);
							Close();
							break;
						default:
							if (mergeGrf != null)
								throw GrfExceptions.__MergeNotSupported.Create();
							break;
					}

					switch(mode) {
						case SavingMode.FileCopy:
						case SavingMode.Compact:
						case SavingMode.Repack:
						case SavingMode.RepackSource:
							try {
								using (FileStream output = new FileStream(fileName, FileMode.Create)) {
									long fileLength = GrfHeader.DataByteSize + InternalHeader.FileTableOffset;

									if (Header.Version < 3.0 && fileLength > uint.MaxValue)
										throw GrfExceptions.__GrfSizeLimitReached.Create();

									output.SetLength(fileLength);

									switch(mode) {
										case SavingMode.FileCopy:
											_table.WriteData(this, Reader.Stream, output, mergeGrf);
											break;
										case SavingMode.Repack:
										case SavingMode.RepackSource:
											_table.WriteDataRepack(this, Reader.Stream, output);
											break;
										case SavingMode.Compact:
											_table.WriteDataCompact(this, Reader.Stream, output);
											break;
									}

									long fileTableOffset = output.Position - GrfHeader.DataByteSize;
									
									if (Header.Version < 3.0 && fileTableOffset > uint.MaxValue)
										throw GrfExceptions.__GrfSizeLimitReached.Create();

									int tableSize = _table.WriteMetadata(InternalHeader, output);
									InternalHeader.FileTableOffset = fileTableOffset;
									InternalHeader.RealFilesCount = Table.Entries.Count;

									output.Seek(0, SeekOrigin.Begin);
									InternalHeader.Write(output);
									output.SetLength(InternalHeader.FileTableOffset + GrfHeader.DataByteSize + tableSize);
								}

								if (!deferExternalEncryptionIndex) _writeEncryptionIndex(fileName);
							}
							catch (OperationCanceledException) {
								if (!exists)
									GrfPath.Delete(fileName);

								throw;
							}
							break;
						case SavingMode.FileEdit:
							try {
								using (FileStream output = new FileStream(FileName, FileMode.OpenOrCreate, FileAccess.ReadWrite)) {
									long offset;

									try {
										offset = _table.WriteDataQuick(this, output, mergeGrf);
									}
									catch (GrfException err) {
										if (err == GrfExceptions.__RepackInstead) {
											Close();
											shouldRepack = true;
											return;
										}
										else {
											throw;
										}
									}

									long fileTableOffset = offset - GrfHeader.DataByteSize;

									if (Header.Version < 3.0 && fileTableOffset > uint.MaxValue)
										throw GrfExceptions.__GrfSizeLimitReached.Create();

									InternalHeader.FileTableOffset = fileTableOffset;
									InternalHeader.RealFilesCount = Table.Entries.Count;

									output.Seek(offset, SeekOrigin.Begin);
									int tableSize = _table.WriteMetadata(InternalHeader, output);
									output.Seek(0, SeekOrigin.Begin);
									InternalHeader.Write(output);
									output.SetLength(InternalHeader.FileTableOffset + GrfHeader.DataByteSize + tableSize);
								}

								_writeEncryptionIndex(fileName);
							}
							catch (OperationCanceledException) {
								if (!exists)
									GrfPath.Delete(fileName);

								throw;
							}
							break;
						case SavingMode.Rgz:
							Rgz.SaveRgz(this, fileName, result);
							break;
						case SavingMode.Thor:
							Thor.SaveFromGrf(this, fileName, result);
							break;
					}
				}
				catch (OperationCanceledException) {
					result.Cancelled();
					return;
				}
				catch (GrfException err) when (err == GrfExceptions.__GrfSizeLimitReached && mergeGrf != null) {
					switch(mode) {
						case SavingMode.FileCopy:
						case SavingMode.Compact:
						case SavingMode.Repack:
						case SavingMode.RepackSource:
							// Temporary file
							try {
								if (fileName != null && File.Exists(fileName))
									File.Delete(fileName);

								Reader?.Close();
								_load(FileName);
							}
							catch {
								Reader?.Open(FileName);
							}
							break;
					}

					throw;
				}
			}
			finally {
				if (mode == SavingMode.FileEdit) {
					try {
						ResetStream();
					}
					catch {
						if (!shouldRepack) throw;
					}
				}
			}
		}

		private void _writeEncryptionIndex(string archivePath) {
			if (!InternalHeader.IsEncrypted)
				return;

			string fileUid = File.GetLastWriteTimeUtc(archivePath).ToFileTimeUtc() + "\\files.enc";
			if (SafeSaveEncryptionIndexWriter != null) SafeSaveEncryptionIndexWriter(fileUid);
			else GrfHolder.WriteEncryptionIndexOrThrow(this, fileUid);
		}

		internal string PrepareDisposableRepackSource(ContainerSaveResult outerResult) {
			string directory = Path.GetDirectoryName(FileName);
			string disposablePath = Path.Combine(directory,
				"." + Path.GetFileName(FileName) + ".repack-" + Guid.NewGuid().ToString("N") + ".grf");
			SafeSaveManifest manifest = SafeSaveManifest.CaptureForRepackedSource(this);
			var writeResult = new ContainerSaveResult(this, disposablePath, null, SavingMode.RepackSource,
				SyncMode.Synchronous);
			SafeSaveOutcome outcome = SafeSaveCoordinator.Execute(new SafeSaveRequest {
				DestinationPath = disposablePath,
				Options = new SafeSaveOptions { CreateBackup = false, IncludeInformationItems = false },
				EstimatedLength = _estimateSafeSaveLength(FileName),
				WriteTemporary = temporary => {
					_internalSave(temporary, null, SavingMode.RepackSource, writeResult,
						deferExternalEncryptionIndex: true);
					if (!writeResult.Success) throw writeResult.Error ?? new IOException("Disposable repack failed.");
					Reader?.Close();
				},
				ValidateTemporary = temporary => SafeSaveValidator(temporary, manifest)
			});

			if (!outcome.Success) {
				outerResult.SafeSaveReport = outcome.Report;
				outerResult.TemporaryFileName = outcome.TemporaryPath;
				throw outcome.Error ?? new IOException("The THOR source archive could not be repacked safely.");
			}

			_quickLoad(disposablePath, SavingMode.RepackSource, ignoreFileType: true);
			return disposablePath;
		}

		internal void ReleaseDisposableRepackSource(string disposablePath, string originalPath) {
			try {
				Reader?.Close();
				FileName = originalPath;
			}
			finally {
				try {
					if (File.Exists(disposablePath)) File.Delete(disposablePath);
				}
				catch {
				}
			}
		}

		/// <summary>
		/// Restores the entry streams.
		/// </summary>
		internal void ResetStream() {
			if (Table == null) return;
			Close();
			Reader.SetStream(GetSharedStream());

			if (Table.Entries.Count > 0) {
				var entries = Table.Entries;

				foreach (FileEntry entry in entries) {
					entry.RefreshStream(Reader);
				}
			}
		}

		private void _save(string fileName, Container mergeGrf, SavingMode mode, ContainerSaveResult result,
			SafeSaveManifest safeSaveManifest, bool mergeAlreadyApplied, ContainerWriteStateSnapshot preOperationState) {
			AProgress.Init(this);
			string destination = fileName ?? FileName;
			string extension = destination.GetExtension();
			if (extension == ".thor" && mode == SavingMode.Thor) {
				_saveThorSafely(destination, mergeGrf, result, preOperationState);
				return;
			}
			if (((extension == ".grf" || extension == ".gpf") &&
				 (mode == SavingMode.FileCopy || mode == SavingMode.Repack || mode == SavingMode.Compact)) ||
				mode == SavingMode.RepackSource) {
				_saveSafely(destination, mergeGrf, mode, result, safeSaveManifest, mergeAlreadyApplied, preOperationState);
				return;
			}
			bool fileCopy = fileName == null && mode != SavingMode.FileEdit;
			string targetFilePath = fileName;
			string sourceFilePath = FileName;

			try {
				if (fileCopy) {
					// Make a file copy first before overwriting the current file
					string tmp = GrfPath.Combine(Path.GetDirectoryName(sourceFilePath), "~" + Path.GetFileName(sourceFilePath));

					// Validate if the source GRF can be deleted, since it needs to be moved afterwards
					string streamFileName = Reader?.Stream?.Name;

					if (streamFileName != null)
						_testIfSourceFileCanBeDeleted(streamFileName);

					targetFilePath = tmp;
				}

				_internalSave(targetFilePath, mergeGrf, mode, result);

				if (!result.Success)
					return;

				if (fileCopy) {
					if (!File.Exists(targetFilePath))
						return;

					Progress = TieredProgress.SpecialCopyingFile;
					Reader?.Close();
					File.Delete(sourceFilePath);
					File.Move(targetFilePath, sourceFilePath);
					targetFilePath = sourceFilePath;
				}

				switch (mode) {
					case SavingMode.Rgz:
					case SavingMode.Thor:
						result.RequiresReload = true;
						break;
					default:
						_quickLoad(targetFilePath, mode, ignoreFileType: true);
						break;
				}
			}
			catch (GrfException err) {
				if (fileCopy)
					_deleteFileCopy(sourceFilePath, targetFilePath);
				result.Fail(err);
			}
			catch (Exception err) {
				if (fileCopy)
					_deleteFileCopy(sourceFilePath, targetFilePath);
				result.Fail(new Exception(GrfStrings.CouldNotSaveContainer, err));
			}
			finally {
				InternalHeader.ThorSettings.Repack = false;
				AProgress.Finalize(this);
			}
		}

		private void _saveThorSafely(string destination, Container mergeGrf, ContainerSaveResult result,
			ContainerWriteStateSnapshot preOperationState) {
			try {
				SafeSaveOutcome outcome = SafeSaveCoordinator.Execute(new SafeSaveRequest {
					DestinationPath = destination,
					Options = SafeSaveOptions ?? new SafeSaveOptions(),
					EstimatedLength = _estimateSafeSaveLength(destination),
					WriteTemporary = temporary => {
						_internalSave(temporary, mergeGrf, SavingMode.Thor, result,
							deferExternalEncryptionIndex: true);
						if (!result.Success) throw result.Error ?? new IOException("THOR write failed.");
						Reader?.Close();
					},
					ValidateTemporary = temporary => SafeThorValidator(temporary)
				});

				result.SafeSaveReport = outcome.Report;
				result.BackupFileName = outcome.BackupPath;
				result.TemporaryFileName = outcome.TemporaryPath;
				if (!outcome.Success) {
					result.Fail(outcome.Error ?? new IOException("Safe THOR save failed validation. " + outcome.Report));
					_restoreState(preOperationState, result, restoreReader: true,
						outcome.Error as SafeSavePromotionException);
					return;
				}

				_reloadFromProvider(destination);
				using (AuthorizeInternalMutation()) Commands.ClearCommands();
			}
			catch (Exception exception) {
				result.Fail(exception);
				_restoreState(preOperationState, result, restoreReader: true, null);
			}
			finally {
				InternalHeader.ThorSettings.Repack = false;
				AProgress.Finalize(this);
			}
		}

		private void _reloadFromProvider(string path) {
			Container loaded = GrfContainerProvider.Get(path);
			Reader?.Close();
			FileName = loaded.FileName;
			Reader = loaded.Reader;
			InternalHeader = loaded.InternalHeader;
			InternalHeader.Rebind(this);
			Header = loaded.Header;
			_table = loaded._table;
			IsNewGrf = loaded.IsNewGrf;
			GC.SuppressFinalize(loaded);
		}

		private static SafeSaveValidationReport ValidateThorArchive(string path) {
			var report = new SafeSaveValidationReport();
			try {
				using (var thor = new Thor(path)) {
					if (thor.Header.Magic != "ASSF (C) 2007 Aeomin DEV")
						report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "thor.magic", path,
							"The generated THOR header is invalid.");
				}
			}
			catch (Exception exception) {
				report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "thor.validation", path,
					exception.Message);
			}
			return report;
		}

		private void _saveSafely(string destination, Container mergeGrf, SavingMode mode, ContainerSaveResult result,
			SafeSaveManifest manifest, bool mergeAlreadyApplied, ContainerWriteStateSnapshot preOperationState) {
			try {
				SafeSaveOutcome outcome = SafeSaveCoordinator.Execute(new SafeSaveRequest {
					DestinationPath = destination,
					Options = SafeSaveOptions ?? new SafeSaveOptions(),
					EstimatedLength = _estimateSafeSaveLength(destination),
					ValidateDestination = (path, exists) => _validateExistingSafeSaveDestination(path, exists, mode),
					ValidateDestinationBeforePromote = (path, exists) => _validateExistingSafeSaveDestination(path, exists, mode),
					CaptureDestinationStamp = path => SafeSaveDestinationStamp.CaptureGrf(path),
					VerifyReplacedDestination = (path, stamp) => _matchesEditableDestinationStamp(path, stamp),
					WriteTemporary = temporary => {
						_internalSave(temporary, mergeGrf, mode, result, mergeAlreadyApplied,
							deferExternalEncryptionIndex: true);
						if (!result.Success) {
							if (result.Error != null) throw result.Error;
							throw new OperationCanceledException();
						}
						Reader?.Close();
					},
					ValidateTemporary = temporary => SafeSaveValidator(temporary, manifest)
				});

				result.SafeSaveReport = outcome.Report;
				result.BackupFileName = outcome.BackupPath;
				result.TemporaryFileName = outcome.TemporaryPath;
				if (!outcome.Success) {
					result.Fail(outcome.Error ?? new IOException("Safe save failed validation. " + outcome.Report));
					SafeSavePromotionException promotionError = outcome.Error as SafeSavePromotionException;
					_restoreState(preOperationState, result, restoreReader: true, promotionError: promotionError);
					return;
				}

				try {
					if (SafeSaveReload != null) SafeSaveReload(destination, mode, true);
					else _quickLoad(destination, mode, ignoreFileType: true);
				}
				catch (Exception reloadError) {
					result.Fail(reloadError is GrfException
						? reloadError
						: new Exception(GrfStrings.CouldNotSaveContainer, reloadError));
					result.RequiresReload = true;
					result.SafeSaveReport.Add(SafeSavePhase.Confirm, SafeSaveSeverity.Error, "reload.failed",
						destination, reloadError.Message);
					return;
				}

				try {
					_writeEncryptionIndex(destination);
				}
				catch (Exception indexError) {
					result.Fail(indexError);
					result.SafeSaveReport.Add(SafeSavePhase.Confirm, SafeSaveSeverity.Error,
						"encryption-index.write-failed", destination, indexError.Message);
				}
			}
			catch (Exception err) {
				result.Fail(err is GrfException ? err : new Exception(GrfStrings.CouldNotSaveContainer, err));
				_restoreState(preOperationState, result, restoreReader: true, promotionError: null);
			}
			finally {
				InternalHeader.ThorSettings.Repack = false;
				AProgress.Finalize(this);
			}
		}

		private static void _validateExistingSafeSaveDestination(string destination, bool destinationExists,
			SavingMode mode) {
			if (!destinationExists) return;
			if (mode == SavingMode.RepackSource && destination.IsExtension(".thor")) return;
			ContainerWriteClassification classification = ContainerWritePolicy.ClassifyFileHeader(destination);
			if (!classification.CanWrite) throw new SafeSaveFormatReadOnlyException(classification);
		}

		private static bool _matchesEditableDestinationStamp(string path, object stamp) {
			var expected = stamp as SafeSaveDestinationStamp;
			if (expected == null) return false;
			SafeSaveDestinationStamp actual = SafeSaveDestinationStamp.CaptureGrf(path);
			return actual.IsEditable && expected.Matches(actual);
		}

		private void _restoreState(ContainerWriteStateSnapshot state, ContainerSaveResult result, bool restoreReader,
			SafeSavePromotionException promotionError) {
			if (state == null) return;
			bool stateRestored = state.TryRestore(this);
			bool readerRestored = !restoreReader || _restoreReaderAfterFailure(promotionError);
			if (stateRestored && readerRestored) return;

			result.RequiresReload = true;
			if (result.SafeSaveReport == null) result.SafeSaveReport = new SafeSaveValidationReport();
			result.SafeSaveReport.Add(SafeSavePhase.Confirm, SafeSaveSeverity.Warning, "state.restore-failed",
				FileName, "The in-memory container state could not be restored completely; reload is required.");
		}

		private bool _restoreReaderAfterFailure(SafeSavePromotionException promotionError) {
			if (promotionError != null && promotionError.RecoveryCode != "promotion.destination-intact" &&
				promotionError.RecoveryCode != "promotion.restored-backup") return false;

			try {
				Reader?.Open(FileName);
				return true;
			}
			catch {
				return false;
			}
		}

		private sealed class ContainerWriteStateSnapshot {
			private readonly long _fileTableOffset;
			private readonly int _realFilesCount;
			private readonly int _tableSize;
			private readonly int _tableSizeCompressed;
			private readonly List<KeyValuePair<string, FileEntry>> _tableEntries;
			private readonly List<string> _lockedFilePaths;
			private readonly List<EntryWriteState> _entries;
			private readonly int _commandIndex;
			private readonly ByteReaderStream _reader;
			private readonly string _fileName;

			private ContainerWriteStateSnapshot(Container container) {
				_fileTableOffset = container.InternalHeader.FileTableOffset;
				_realFilesCount = container.InternalHeader.RealFilesCount;
				_tableSize = container.InternalTable.TableSize;
				_tableSizeCompressed = container.InternalTable.TableSizeCompressed;
				_tableEntries = container.InternalTable.CaptureEntryReferences();
				_lockedFilePaths = container.InternalTable.CaptureLockedFilePaths();
				_entries = _tableEntries.Select(item => item.Value).Distinct()
					.Select(entry => new EntryWriteState(entry)).ToList();
				_commandIndex = container.Commands.CommandIndex;
				_reader = container.Reader;
				_fileName = container.FileName;
			}

			internal static ContainerWriteStateSnapshot Capture(Container container) {
				return new ContainerWriteStateSnapshot(container);
			}

			internal bool TryRestore(Container container) {
				try {
					container.Reader = _reader;
					container.FileName = _fileName;
					container.InternalHeader.FileTableOffset = _fileTableOffset;
					container.InternalHeader.RealFilesCount = _realFilesCount;
					container.InternalTable.TableSize = _tableSize;
					container.InternalTable.TableSizeCompressed = _tableSizeCompressed;
					foreach (EntryWriteState entry in _entries) entry.Restore();
					bool restored = container.InternalTable.RestoreEntryReferences(_tableEntries, _lockedFilePaths);
					int extraCommands = container.Commands.CommandIndex - _commandIndex;
					if (extraCommands > 0) container.Commands.RemoveCommands(extraCommands);
					return restored;
				}
				catch {
					return false;
				}
			}
		}

		private sealed class EntryWriteState {
			private readonly FileEntry _entry;
			private readonly long _temporaryOffset;
			private readonly int _temporarySizeCompressedAlignment;
			private readonly int _newSizeCompressed;
			private readonly int _newSizeDecompressed;
			private readonly long _fileExactOffset;
			private readonly long _offset;
			private readonly int _sizeCompressed;
			private readonly int _sizeCompressedAlignment;
			private readonly int _sizeDecompressed;
			private readonly int _removedFlagCount;
			private readonly EntryType _flags;
			private readonly string _sourceFilePath;
			private readonly string _relativePath;
			private readonly Modification _modification;
			private readonly MultiType _rawDataSource;
			private readonly GrfHeader _header;
			private readonly ByteReaderStream _stream;
			private readonly bool _bypassSaveCheck;
			private readonly string _extractionFilePath;
			private readonly object _dataImage;
			private readonly int _cycle;

			internal EntryWriteState(FileEntry entry) {
				_entry = entry;
				_temporaryOffset = entry.TemporaryOffset;
				_temporarySizeCompressedAlignment = entry.TemporarySizeCompressedAlignment;
				_newSizeCompressed = entry.NewSizeCompressed;
				_newSizeDecompressed = entry.NewSizeDecompressed;
				_fileExactOffset = entry.FileExactOffset;
				_offset = entry.Offset;
				_sizeCompressed = entry.SizeCompressed;
				_sizeCompressedAlignment = entry.SizeCompressedAlignment;
				_sizeDecompressed = entry.SizeDecompressed;
				_removedFlagCount = entry.RemovedFlagCount;
				_flags = entry.Flags;
				_sourceFilePath = entry.SourceFilePath;
				_relativePath = entry.RelativePath;
				_modification = entry.Modification;
				_rawDataSource = entry.RawDataSource;
				_header = entry.Header;
				_stream = entry.Stream;
				_bypassSaveCheck = entry.BypassSaveCheck;
				_extractionFilePath = entry.ExtractionFilePath;
				_dataImage = entry.DataImage;
				_cycle = entry.Cycle;
			}

			internal void Restore() {
				_entry.TemporaryOffset = _temporaryOffset;
				_entry.TemporarySizeCompressedAlignment = _temporarySizeCompressedAlignment;
				_entry.NewSizeCompressed = _newSizeCompressed;
				_entry.NewSizeDecompressed = _newSizeDecompressed;
				_entry.FileExactOffset = _fileExactOffset;
				_entry.Offset = _offset;
				_entry.SizeCompressed = _sizeCompressed;
				_entry.SizeCompressedAlignment = _sizeCompressedAlignment;
				_entry.SizeDecompressed = _sizeDecompressed;
				_entry.RemovedFlagCount = _removedFlagCount;
				_entry.Flags = _flags;
				_entry.SourceFilePath = _sourceFilePath;
				_entry.RelativePath = _relativePath;
				_entry.Modification = _modification;
				_entry.RawDataSource = _rawDataSource;
				_entry.Header = _header;
				_entry.Stream = _stream;
				_entry.BypassSaveCheck = _bypassSaveCheck;
				_entry.ExtractionFilePath = _extractionFilePath;
				_entry.DataImage = _dataImage;
				_entry.Cycle = _cycle;
			}
		}

		private long _estimateSafeSaveLength(string destination) {
			try {
				if (File.Exists(destination)) return Math.Max(0, new FileInfo(destination).Length);
				if (File.Exists(FileName)) return Math.Max(0, new FileInfo(FileName).Length);
			}
			catch {
			}
			return GrfHeader.DataByteSize;
		}

		private void _deleteFileCopy(string sourceFilePath, string targetFilePath) {
			try {
				if (File.Exists(sourceFilePath) && File.Exists(targetFilePath))
					GrfPath.Delete(targetFilePath);
			}
			catch { }
		}

		private void _testIfSourceFileCanBeDeleted(string streamFileName) {
			try {
				Reader?.Close();

				if (Methods.IsFileLocked(streamFileName))
					throw GrfExceptions.__FileLocked.Create(streamFileName);
			}
			finally {
				Reader?.Open(streamFileName);
			}
		}

		private void _quickLoad(string fileName, SavingMode mode, bool ignoreFileType = false) {
			// Thor and Rgz use temporary archives, not reloading them is problematic
			if (!ignoreFileType && fileName.IsExtension(".thor", ".rgz")) {
				return;
			}

			if (fileName != null) {
				FileName = fileName;
				Reader?.Close();
				Reader = new ByteReaderStream(fileName);
			}

			var entries = Table.Entries;

			for (int i = 0; i < entries.Count; i++) {
				var entry = entries[i];
				bool propertyChanged = false;

				if (entry.Modification.HasFlag(Modification.Removed)) {
					Table.DeleteEntry(entry.RelativePath);
					continue;
				}

				if (entry.Modification.HasFlag(Modification.Encrypt)) {
					entry.Flags |= EntryType.GrfEditorCrypted;
				}
				else if (entry.Modification.HasFlag(Modification.Decrypt)) {
					entry.Flags &= ~EntryType.GrfEditorCrypted;
				}
				else if (entry.Modification.HasFlag(Modification.Added)) {
					propertyChanged = true;
				}

				entry.Modification &= ~(Modification.GrfMerge | Modification.Encrypt | Modification.Decrypt | Modification.Added);
				entry.ExtractionFilePath = null;
				entry.SourceFilePath = null;
				entry.RawDataSource = null;
				entry.RemovedFlagCount = 0;

				entry.SizeCompressed = entry.NewSizeCompressed;
				entry.SizeCompressedAlignment = entry.TemporarySizeCompressedAlignment;
				entry.SizeDecompressed = entry.NewSizeDecompressed;
				entry.FileExactOffset = entry.TemporaryOffset;

				entry.Stream = Reader;
				entry.Header = InternalHeader;

				if (propertyChanged)
					entry.OnPropertyChanged("IsAdded");
			}

			Table.InvalidateInternalSets();
			Commands.ClearCommands();
		}

		/// <summary>
		/// Gets the shared stream.
		/// </summary>
		/// <returns></returns>
		public override DisposableScope<FileStream> GetSharedStream() {
			return new DisposableScope<FileStream>(File.Exists(FileName) ? new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
		}

		/// <summary>
		/// Gets the source stream.
		/// </summary>
		/// <returns></returns>
		public override DisposableScope<FileStream> GetSourceStream() {
			try {
				return new DisposableScope<FileStream>(File.Exists(Reader.Stream.Name) ? new FileStream(Reader.Stream.Name, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
			}
			catch {
				return GetSharedStream();
			}
		}

		private void _applyMergeOnTable(Container grfAdd) {
			if (grfAdd == null)
				return;

			foreach (FileEntry entry in grfAdd.Table.Entries) {
				if (Table.ContainsKey(entry.RelativePath)) {
					// Ensures all streams are closed... again?
					Table.DeleteFile(entry.RelativePath);
				}
			}

			foreach (FileEntry entry in grfAdd.Table.Entries) {
				Table.AddEntry(new FileEntry(entry) {Modification = Modification.GrfMerge});
			}

			foreach (FileEntry entry in grfAdd.Table.Entries.Where(p => p.Flags.HasFlags(EntryType.RemoveFile))) {
				Table.DeleteFile(entry.RelativePath);
			}

			Table.InvalidateInternalSets();
		}

		private void _headerEncryptedFailed(string fileName) {
			if ((InternalHeader.IsEncrypted || InternalHeader.EncryptFileTable) && (fileName.IsExtension(".rgz") || Header.IsMajorVersion(1)))
				throw GrfExceptions.__UnsupportedEncryptionVersion.Create();
		}

		private bool _headerCheckFailed() {
			if (InternalHeader.FoundErrors) {
				if (ErrorHandler.YesNoRequest(GrfStrings.GrfContainsErrors, GrfStrings.GrfDataIntegrity) == false) {
					return true;
				}
			}

			return false;
		}

		public void Close() {
			Reader?.Close();
		}

		/// <summary>
		/// Gets the stream raw data. Use carefully.
		/// </summary>
		/// <param name="entry">The entry.</param>
		/// <returns></returns>
		public byte[] GetStreamRawData(FileEntry entry) {
			byte[] data;

			lock (Reader.SharedLock) {
				Reader.PositionLong = entry.FileExactOffset;
				data = Reader.Bytes(entry.SizeCompressedAlignment);
			}

			return data;
		}

		/// <summary>
		/// Executes an operation on the GRF entries on multiple threads.
		/// </summary>
		/// <param name="progress">The progress method.</param>
		/// <param name="isCancelling">The cancelling method.</param>
		/// <param name="action">The action.</param>
		public void ThreadOperation(Action<float> progress, Func<bool> isCancelling, Action<FileEntry, byte[]> action) {
			ThreadOperation(progress, isCancelling, action, -1);
		}

		/// <summary>
		/// Executes an operation on the GRF entries on multiple threads.
		/// </summary>
		/// <param name="progress">The progress method.</param>
		/// <param name="isCancelling">The cancelling method.</param>
		/// <param name="action">The action.</param>
		/// <param name="numOfThreads">The number of threads.</param>
		public void ThreadOperation(Action<float> progress, Func<bool> isCancelling, Action<FileEntry, byte[]> action, int numOfThreads) {
			GrfThreadPool<FileEntry> threadPool = new GrfThreadPool<FileEntry>();
			threadPool.Initialize<ThreadGenericGrf>(this, Table.Entries, numOfThreads);
			foreach (var thread in threadPool.Threads.OfType<ThreadGenericGrf>()) {
				thread.Init(action, isCancelling);
			}
			threadPool.Start(progress, isCancelling);
		}
	}
}
