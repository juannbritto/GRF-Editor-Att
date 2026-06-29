using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GRF.Core.SafeSave {
	internal sealed class SafeSavePathLockManager {
		private readonly object _sync = new object();
		private readonly Dictionary<string, Entry> _entries =
			new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
		private readonly ISafeSavePathIdentityResolver _identityResolver;

		internal SafeSavePathLockManager() : this(new SafeSavePathIdentityResolver()) {
		}

		internal SafeSavePathLockManager(ISafeSavePathIdentityResolver identityResolver) {
			_identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
		}

		internal int ActiveEntryCount {
			get {
				lock (_sync) return _entries.Count;
			}
		}

		internal IDisposable Acquire(IEnumerable<string> paths) {
			if (paths == null) throw new ArgumentNullException(nameof(paths));
			string[] identities = paths
				.Select(_identityResolver.Resolve)
				.ToArray();
			return AcquireIdentities(identities);
		}

		internal IDisposable AcquireIdentities(IEnumerable<string> identities) {
			if (identities == null) throw new ArgumentNullException(nameof(identities));
			string[] identitySnapshot = identities
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var entries = ReserveEntries(identitySnapshot);
			var acquired = new List<AcquiredEntry>(entries.Length);

			try {
				foreach (Entry entry in entries) acquired.Add(AcquireEntry(entry));
				return new Lease(this, entries, acquired.ToArray());
			}
			catch {
				try {
					ReleaseAcquired(acquired);
				}
				finally {
					ReleaseReferences(entries);
				}
				throw;
			}
		}

		private Entry[] ReserveEntries(string[] identities) {
			var entries = new Entry[identities.Length];
			lock (_sync) {
				for (int index = 0; index < identities.Length; index++) {
					Entry entry;
					if (!_entries.TryGetValue(identities[index], out entry)) {
						entry = new Entry(identities[index]);
						_entries.Add(identities[index], entry);
					}
					entry.ReferenceCount++;
					entries[index] = entry;
				}
			}
			return entries;
		}

		private static AcquiredEntry AcquireEntry(Entry entry) {
			entry.Gate.Wait();
			Mutex mutex = null;
			bool ownsMutex = false;
			try {
				mutex = new Mutex(false, GetMutexName(entry.Identity));
				try {
					mutex.WaitOne();
					ownsMutex = true;
				}
				catch (AbandonedMutexException) {
					ownsMutex = true;
				}
				return new AcquiredEntry(entry, mutex);
			}
			catch {
				if (ownsMutex) mutex.ReleaseMutex();
				if (mutex != null) mutex.Dispose();
				entry.Gate.Release();
				throw;
			}
		}

		internal static string GetMutexName(string identity) {
			byte[] bytes = Encoding.UTF8.GetBytes(identity.ToUpperInvariant());
			using (SHA256 sha256 = SHA256.Create()) {
				return @"Global\GRFEditor.SafeSave." + BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", string.Empty);
			}
		}

		private void Release(Entry[] entries, IList<AcquiredEntry> acquired) {
			try {
				ReleaseAcquired(acquired);
			}
			finally {
				ReleaseReferences(entries);
			}
		}

		private static void ReleaseAcquired(IList<AcquiredEntry> acquired) {
			Exception firstFailure = null;
			for (int index = acquired.Count - 1; index >= 0; index--) {
				AcquiredEntry item = acquired[index];
				try {
					item.Mutex.ReleaseMutex();
				}
				catch (Exception exception) {
					if (firstFailure == null) firstFailure = exception;
				}
				finally {
					item.Mutex.Dispose();
					item.Entry.Gate.Release();
				}
			}
			if (firstFailure != null) throw firstFailure;
		}

		private void ReleaseReferences(IEnumerable<Entry> entries) {
			lock (_sync) {
				foreach (Entry entry in entries) {
					entry.ReferenceCount--;
					if (entry.ReferenceCount == 0) _entries.Remove(entry.Identity);
				}
			}
		}

		private sealed class Entry {
			internal readonly string Identity;
			internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
			internal int ReferenceCount;

			internal Entry(string identity) {
				Identity = identity;
			}
		}

		private sealed class AcquiredEntry {
			internal readonly Entry Entry;
			internal readonly Mutex Mutex;

			internal AcquiredEntry(Entry entry, Mutex mutex) {
				Entry = entry;
				Mutex = mutex;
			}
		}

		private sealed class Lease : IDisposable {
			private SafeSavePathLockManager _owner;
			private Entry[] _entries;
			private AcquiredEntry[] _acquired;

			internal Lease(SafeSavePathLockManager owner, Entry[] entries, AcquiredEntry[] acquired) {
				_owner = owner;
				_entries = entries;
				_acquired = acquired;
			}

			public void Dispose() {
				SafeSavePathLockManager owner = Interlocked.Exchange(ref _owner, null);
				if (owner == null) return;
				Entry[] entries = _entries;
				AcquiredEntry[] acquired = _acquired;
				_entries = null;
				_acquired = null;
				owner.Release(entries, acquired);
			}
		}
	}
}
