# GRF Editor Safe Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make classic GRF/GPF saves transactional, validated, recoverable, and blocked for protected or unknown headers while shipping a separately branded GRF Editor Safe application.

**Architecture:** Keep the upstream GRF parser/writer and place a capability policy plus a transaction coordinator around `Container._internalSave`. The coordinator writes beside the destination, validates a logical manifest and structural metadata, then promotes with `File.Replace`; WPF consumes the same core capability and report objects so UI disabling cannot be bypassed by another save entry point.

**Tech Stack:** C#; .NET Standard 2.0 GRF library; .NET Framework 4.8 WPF; MSTest 4.2.3; Microsoft.NET.Test.Sdk 18.7.0; PowerShell; Inno Setup.

---

## File map

- `GRF/Core/SafeSave/ContainerWriteCapability.cs`: write-state enum and immutable classification result.
- `GRF/Core/SafeSave/ContainerWritePolicy.cs`: single policy for classic, protected, and unknown headers.
- `GRF/Core/SafeSave/SafeSaveOptions.cs`: backup and validation options supplied by library and UI callers.
- `GRF/Core/SafeSave/SafeSaveManifest.cs`: expected logical paths, sizes, and SHA-256 hashes.
- `GRF/Core/SafeSave/SafeSaveValidationReport.cs`: phase-aware information, warning, and error records.
- `GRF/Core/SafeSave/SafeGrfValidator.cs`: structural and logical validation of a closed temporary GRF.
- `GRF/Core/SafeSave/ISafeSaveFileSystem.cs`: narrow filesystem seam for deterministic transaction tests.
- `GRF/Core/SafeSave/SafeSaveFileSystem.cs`: production implementation based on `File.Replace` and same-volume moves.
- `GRF/Core/SafeSave/SafeSaveCoordinator.cs`: write, validate, backup, promote, cleanup, and recovery orchestration.
- `GRF/Core/Container.cs`: route GRF/GPF writes through the coordinator instead of direct `FileEdit` or delete-then-move.
- `GRF/Core/GrfHolder.cs`: expose capability, safe save options, report, and safe defaults.
- `GRF/ContainerFormat/ContainerSaveResult.cs`: return report and backup path to callers.
- `GRF/Properties/InternalsVisibleTo.cs`: expose internals only to the test assembly.
- `GRF.SafeSave.Tests/*`: unit and integration tests.
- `scripts/prepare-grf-safe-fixtures.ps1`: copy real samples into ignored workspace artifacts and hash the originals.
- `GRF/Core/SafeSave/SafeSaveUiState.cs`: presentation state independent of WPF controls.
- `GRF/Core/SafeSave/SafeSaveUiText.cs`: Portuguese and English safe-save strings.
- `GRFEditor/WPF/SafeSaveReportDialog.xaml*`: detailed validation report.
- `GRFEditor/EditorMainWindow.xaml`, `EditorMainWindow.xaml.cs`, `EMenuInteraction.cs`: state badge, safe commands, progress, and report handling.
- `GRFEditor/ApplicationConfiguration/GrfEditorConfiguration.cs`, `GRFEditor/WPF/SettingsDialog.xaml`: safe-save preferences.
- `GRFEditor/Properties/AssemblyInfo.cs`, `GRFEditor/GRFEditor.csproj`, `GrfEditor.iss`: side-by-side branding and installation.

### Task 1: Establish a repeatable build and test harness

**Files:**
- Create: `GRF.SafeSave.Tests/GRF.SafeSave.Tests.csproj`
- Create: `GRF.SafeSave.Tests/SmokeTests.cs`
- Create: `GRF/Properties/InternalsVisibleTo.cs`
- Modify: `.gitignore`

- [ ] **Step 1: Install the local build prerequisites if they are absent**

Run:

```powershell
if (-not (Test-Path "$env:ProgramFiles\dotnet\dotnet.exe")) {
  winget install --exact --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
}
if (-not (Test-Path "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe")) {
  winget install --exact --id Microsoft.VisualStudio.2022.BuildTools --accept-source-agreements --accept-package-agreements --override "--wait --passive --add Microsoft.VisualStudio.Workload.ManagedDesktopBuildTools --includeRecommended"
}
```

Expected: .NET SDK 8 and MSBuild 17 are available; the already installed .NET Framework 4.8 runtime remains unchanged.

- [ ] **Step 2: Create the test project and a failing assembly-access smoke test**

Create `GRF.SafeSave.Tests/GRF.SafeSave.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
    <AssemblyName>GRF.SafeSave.Tests</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.7.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="4.2.3" />
    <PackageReference Include="MSTest.TestFramework" Version="4.2.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GRF\GRF.csproj" />
  </ItemGroup>
</Project>
```

Create `GRF.SafeSave.Tests/SmokeTests.cs`:

```csharp
using GRF.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
    [TestClass]
    public class SmokeTests {
        [TestMethod]
        public void New_container_uses_classic_header() {
            using (var holder = new GrfHolder()) {
                holder.New("smoke.grf");
                Assert.AreEqual("Master of Magic\0", holder.Header.Magic);
            }
        }
    }
}
```

- [ ] **Step 3: Run the smoke test and confirm the expected access/build failure**

Run:

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --no-restore
```

Expected: FAIL because packages are not restored or the test assembly cannot yet access planned internal seams. A failure before test discovery must be resolved by the restore command in the next step, not hidden.

- [ ] **Step 4: Add test visibility and ignore generated fixtures/results**

Create `GRF/Properties/InternalsVisibleTo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GRF.SafeSave.Tests")]
```

Append to `.gitignore`:

```gitignore
artifacts/grf-safe-fixtures/
**/TestResults/
```

Restore and test:

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" restore GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --no-restore
```

Expected: PASS, 1 test.

- [ ] **Step 5: Commit the harness**

```powershell
git add .gitignore GRF.SafeSave.Tests GRF/Properties/InternalsVisibleTo.cs
git commit -m "test: add safe save harness"
```

### Task 2: Classify writable, protected, and unknown containers

**Files:**
- Create: `GRF/Core/SafeSave/ContainerWriteCapability.cs`
- Create: `GRF/Core/SafeSave/ContainerWritePolicy.cs`
- Create: `GRF.SafeSave.Tests/ContainerWritePolicyTests.cs`
- Modify: `GRF/Core/GrfHolder.cs`

- [ ] **Step 1: Write failing policy tests**

Create `GRF.SafeSave.Tests/ContainerWritePolicyTests.cs`:

```csharp
using GRF;
using GRF.Core;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
    [TestClass]
    public class ContainerWritePolicyTests {
        [TestMethod]
        public void Classic_supported_header_is_editable() {
            var result = ContainerWritePolicy.Classify(GrfStrings.MasterOfMagic, 2, 0, false);
            Assert.AreEqual(ContainerWriteCapability.Editable, result.Capability);
        }

        [TestMethod]
        public void Event_horizon_is_protected_even_when_version_is_supported() {
            var result = ContainerWritePolicy.Classify(GrfStrings.EventHorizon, 3, 0, false);
            Assert.AreEqual(ContainerWriteCapability.ReadOnlyProtected, result.Capability);
        }

        [TestMethod]
        public void Unknown_magic_or_header_errors_are_read_only() {
            Assert.AreEqual(ContainerWriteCapability.ReadOnlyUnknown,
                ContainerWritePolicy.Classify("Unknown header\0", 2, 0, false).Capability);
            Assert.AreEqual(ContainerWriteCapability.ReadOnlyUnknown,
                ContainerWritePolicy.Classify(GrfStrings.MasterOfMagic, 2, 0, true).Capability);
        }
    }
}
```

- [ ] **Step 2: Run the policy tests and watch them fail**

Run:

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ContainerWritePolicyTests
```

Expected: FAIL with `CS0234` because `GRF.Core.SafeSave` does not exist.

- [ ] **Step 3: Implement the policy and expose it from `GrfHolder`**

Create `GRF/Core/SafeSave/ContainerWriteCapability.cs`:

```csharp
namespace GRF.Core.SafeSave {
    public enum ContainerWriteCapability {
        Editable,
        ReadOnlyProtected,
        ReadOnlyUnknown
    }

    public sealed class ContainerWriteClassification {
        public ContainerWriteClassification(ContainerWriteCapability capability, string reasonCode) {
            Capability = capability;
            ReasonCode = reasonCode;
        }

        public ContainerWriteCapability Capability { get; }
        public string ReasonCode { get; }
        public bool CanWrite => Capability == ContainerWriteCapability.Editable;
    }
}
```

Create `GRF/Core/SafeSave/ContainerWritePolicy.cs`:

```csharp
namespace GRF.Core.SafeSave {
    public static class ContainerWritePolicy {
        public static ContainerWriteClassification Classify(string magic, byte major, byte minor, bool foundErrors) {
            if (magic == GrfStrings.EventHorizon)
                return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyProtected, "event-horizon");

            bool supportedVersion = (major == 1 && (minor == 2 || minor == 3)) ||
                                    (major == 2 && minor == 0);
            if (magic == GrfStrings.MasterOfMagic && supportedVersion && !foundErrors)
                return new ContainerWriteClassification(ContainerWriteCapability.Editable, "classic-supported");

            return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyUnknown,
                foundErrors ? "header-errors" : "unknown-format");
        }

        public static ContainerWriteClassification Classify(GrfHeader header) {
            return Classify(header.Magic, header.MajorVersion, header.MinorVersion, header.FoundErrors);
        }
    }
}
```

Add to `GrfHolder`:

```csharp
public ContainerWriteClassification WriteClassification => ContainerWritePolicy.Classify(Header);
public bool CanWriteSafely => WriteClassification.CanWrite;
```

- [ ] **Step 4: Run all tests**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: PASS, 4 tests.

- [ ] **Step 5: Commit the policy**

```powershell
git add GRF/Core/SafeSave GRF/Core/GrfHolder.cs GRF.SafeSave.Tests/ContainerWritePolicyTests.cs
git commit -m "feat: classify GRF write capability"
```

### Task 3: Build logical manifests and structural validation reports

**Files:**
- Create: `GRF/Core/SafeSave/SafeSaveManifest.cs`
- Create: `GRF/Core/SafeSave/SafeSaveValidationReport.cs`
- Create: `GRF/Core/SafeSave/SafeGrfValidator.cs`
- Create: `GRF.SafeSave.Tests/SafeGrfValidatorTests.cs`

- [ ] **Step 1: Write failing manifest and validator tests**

Create `GRF.SafeSave.Tests/SafeGrfValidatorTests.cs` with a helper that creates a real tiny GRF through the upstream writer:

```csharp
using System;
using System.IO;
using GRF.Core;
using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
    [TestClass]
    public class SafeGrfValidatorTests {
        private string _dir;

        [TestInitialize]
        public void SetUp() {
            _dir = Path.Combine(Path.GetTempPath(), "grf-safe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TestCleanup]
        public void TearDown() {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        [TestMethod]
        public void Valid_grf_matches_expected_logical_manifest() {
            string path = Path.Combine(_dir, "valid.grf");
            using (var holder = new GrfHolder()) {
                holder.New(path);
                holder.Commands.AddFile("data\\hello.txt", new byte[] { 1, 2, 3 });
                Assert.IsTrue(holder.SaveAs(path).Success);
            }

            SafeSaveManifest expected;
            using (var holder = new GrfHolder(path)) expected = SafeSaveManifest.Capture(holder);
            var report = new SafeGrfValidator().Validate(path, expected);

            Assert.IsFalse(report.HasErrors, report.ToString());
        }

        [TestMethod]
        public void Truncated_grf_reports_structural_error() {
            string path = Path.Combine(_dir, "truncated.grf");
            File.WriteAllBytes(path, new byte[20]);
            var report = new SafeGrfValidator().Validate(path, null);
            Assert.IsTrue(report.HasErrors);
            Assert.IsTrue(report.Items.Exists(x => x.Code == "header.invalid"));
        }
    }
}
```

- [ ] **Step 2: Verify the tests fail for missing types**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeGrfValidatorTests
```

Expected: FAIL with `CS0246` for `SafeSaveManifest`.

- [ ] **Step 3: Implement the report model**

Create `GRF/Core/SafeSave/SafeSaveValidationReport.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace GRF.Core.SafeSave {
    public enum SafeSavePhase { Preflight, WriteTemporary, Validate, Backup, Promote, Confirm }
    public enum SafeSaveSeverity { Information, Warning, Error }

    public sealed class SafeSaveValidationItem {
        public SafeSaveValidationItem(SafeSavePhase phase, SafeSaveSeverity severity, string code, string path, string message) {
            Phase = phase; Severity = severity; Code = code; Path = path; Message = message;
        }
        public SafeSavePhase Phase { get; }
        public SafeSaveSeverity Severity { get; }
        public string Code { get; }
        public string Path { get; }
        public string Message { get; }
    }

    public sealed class SafeSaveValidationReport {
        public List<SafeSaveValidationItem> Items { get; } = new List<SafeSaveValidationItem>();
        public bool HasErrors => Items.Any(x => x.Severity == SafeSaveSeverity.Error);
        public void Add(SafeSavePhase phase, SafeSaveSeverity severity, string code, string path, string message) =>
            Items.Add(new SafeSaveValidationItem(phase, severity, code, path, message));
        public override string ToString() => string.Join("\r\n", Items.Select(x => $"[{x.Severity}] {x.Code}: {x.Message}"));
    }
}
```

- [ ] **Step 4: Implement manifest capture and validator**

Create `GRF/Core/SafeSave/SafeSaveManifest.cs` using `SHA256.Create().ComputeHash(entry.GetDecompressedData())`, excluding entries with `Modification.Removed` and storing entries in `Dictionary<string, SafeSaveManifestEntry>(StringComparer.OrdinalIgnoreCase)`. Each entry stores `RelativePath`, `SizeDecompressed`, and the hash byte array. Provide both signatures used by later tasks:

```csharp
public static SafeSaveManifest Capture(GrfHolder holder) => Capture(holder.Container);

internal static SafeSaveManifest Capture(Container container) {
    var result = new SafeSaveManifest();
    foreach (var entry in container.Table.Entries.Where(x => !x.Modification.HasFlag(Modification.Removed))) {
        using (var sha = SHA256.Create()) {
            result.Entries.Add(entry.RelativePath, new SafeSaveManifestEntry(
                entry.RelativePath,
                entry.GetSizeDecompressed(),
                sha.ComputeHash(entry.GetDecompressedData())));
        }
    }
    return result;
}

public void Compare(GrfHolder actual, SafeSaveValidationReport report) {
    var actualEntries = actual.FileTable.Entries.ToDictionary(x => x.RelativePath, StringComparer.OrdinalIgnoreCase);
    foreach (var expected in Entries.Values) {
        if (!actualEntries.TryGetValue(expected.RelativePath, out var entry)) {
            report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.missing", expected.RelativePath, "Expected entry is missing.");
            continue;
        }
        if (entry.SizeDecompressed != expected.SizeDecompressed)
            report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.size", expected.RelativePath, "Decompressed size changed.");
        using (var sha = SHA256.Create()) {
            if (!sha.ComputeHash(entry.GetDecompressedData()).SequenceEqual(expected.Sha256))
                report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.hash", expected.RelativePath, "Decompressed content changed.");
        }
        actualEntries.Remove(expected.RelativePath);
    }
    foreach (var unexpected in actualEntries.Keys)
        report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "manifest.unexpected", unexpected, "Unexpected entry was written.");
}
```

Create `GRF/Core/SafeSave/SafeGrfValidator.cs` with this validation order:

```csharp
public SafeSaveValidationReport Validate(string path, SafeSaveManifest expected) {
    var report = new SafeSaveValidationReport();
    if (!File.Exists(path) || new FileInfo(path).Length < GrfHeader.DataByteSize) {
        report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "header.invalid", path, "GRF header is missing or truncated.");
        return report;
    }

    try {
        using (var holder = new GrfHolder(path)) {
            var classification = holder.WriteClassification;
            if (!classification.CanWrite)
                report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "format.not-editable", path, classification.ReasonCode);

            long tableStart = holder.Header.FileTableOffset + GrfHeader.DataByteSize;
            long fileLength = new FileInfo(path).Length;
            foreach (var entry in holder.FileTable.Entries) {
                if (entry.FileExactOffset < GrfHeader.DataByteSize ||
                    entry.SizeCompressed < 0 ||
                    entry.SizeCompressedAlignment < entry.SizeCompressed ||
                    entry.SizeDecompressed < 0 ||
                    entry.FileExactOffset + entry.SizeCompressedAlignment > tableStart ||
                    tableStart > fileLength) {
                    report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.bounds", entry.RelativePath, "Entry metadata points outside the GRF data region.");
                    continue;
                }

                byte[] data = entry.GetDecompressedData();
                if (data.LongLength != entry.SizeDecompressed)
                    report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "entry.size", entry.RelativePath, "Decompressed size differs from metadata.");
            }

            if (expected != null) expected.Compare(holder, report);
        }
    }
    catch (Exception error) {
        report.Add(SafeSavePhase.Validate, SafeSaveSeverity.Error, "validation.exception", path, error.Message);
    }
    return report;
}
```

`SafeSaveManifest.Compare` must report `manifest.missing`, `manifest.unexpected`, `manifest.size`, and `manifest.hash` and must hash one entry at a time so a complete GRF is never loaded into one byte array.

- [ ] **Step 5: Run validator tests and the complete suite**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeGrfValidatorTests
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: both commands PASS.

- [ ] **Step 6: Commit manifest validation**

```powershell
git add GRF/Core/SafeSave GRF.SafeSave.Tests/SafeGrfValidatorTests.cs
git commit -m "feat: validate saved GRF manifests"
```

### Task 4: Implement atomic promotion, backup preservation, and cleanup

**Files:**
- Create: `GRF/Core/SafeSave/ISafeSaveFileSystem.cs`
- Create: `GRF/Core/SafeSave/SafeSaveFileSystem.cs`
- Create: `GRF/Core/SafeSave/SafeSaveCoordinator.cs`
- Create: `GRF/Core/SafeSave/SafeSaveOptions.cs`
- Create: `GRF.SafeSave.Tests/SafeSaveCoordinatorTests.cs`

- [ ] **Step 1: Write failing coordinator tests with an in-memory filesystem fake**

Create tests named:

```csharp
[TestMethod] public void Validation_failure_never_replaces_destination()
[TestMethod] public void Existing_destination_is_atomically_replaced_and_backed_up()
[TestMethod] public void Promotion_failure_restores_previous_backup_and_keeps_destination()
[TestMethod] public void Cancelled_write_removes_only_owned_temporary_file()
```

The fake records `WriteTemporary`, `Replace`, `Move`, `Delete`, and `RestoreBackup` calls. In `Validation_failure_never_replaces_destination`, return a report containing one `Error` and assert zero `Replace`/`Move` calls and one deletion of the coordinator-generated `.tmp` path.

- [ ] **Step 2: Run and confirm missing coordinator failures**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveCoordinatorTests
```

Expected: FAIL with `CS0246` for `SafeSaveCoordinator`.

- [ ] **Step 3: Implement options and the filesystem seam**

Create `SafeSaveOptions` with these defaults:

```csharp
public sealed class SafeSaveOptions {
    public bool CreateBackup { get; set; } = true;
    public bool IncludeInformationItems { get; set; } = true;
    public string BackupSuffix { get; set; } = ".bak";
    public Action<SafeSavePhase> PhaseChanged { get; set; }
}
```

`ISafeSaveFileSystem` must use these exact signatures:

```csharp
internal interface ISafeSaveFileSystem {
    bool Exists(string path);
    long Length(string path);
    long AvailableFreeSpace(string directoryPath);
    void DeleteOwnedTemporary(string path);
    void MoveNew(string temporaryPath, string destinationPath);
    void ReplaceExisting(string temporaryPath, string destinationPath, string backupPath);
}
```

`SafeSaveFileSystem.ReplaceExisting` must:

1. move an existing `.bak` to `.bak.previous-safe-save`;
2. call `File.Replace(temp, destination, backup, true)`;
3. delete `.bak.previous-safe-save` after success;
4. restore `.bak.previous-safe-save` if replacement throws and the normal backup is absent.

No method accepts a directory for recursive deletion.

- [ ] **Step 4: Implement the transaction coordinator**

Use this request boundary:

```csharp
internal sealed class SafeSaveRequest {
    public string DestinationPath { get; set; }
    public SafeSaveOptions Options { get; set; }
    public Action<string> WriteTemporary { get; set; }
    public Func<string, SafeSaveValidationReport> ValidateTemporary { get; set; }
}

internal sealed class SafeSaveOutcome {
    public bool Success { get; set; }
    public string TemporaryPath { get; set; }
    public string BackupPath { get; set; }
    public SafeSaveValidationReport Report { get; set; }
    public Exception Error { get; set; }
}
```

`SafeSaveCoordinator.Execute` must calculate `destination + ".safe-save-" + Guid.NewGuid().ToString("N") + ".tmp"`, check same-directory free space against `Math.Max(sourceLength, 64 * 1024)`, invoke `PhaseChanged` before preflight/write/validate/backup/promote/confirm, call the delegates, stop on validation error, then call `ReplaceExisting` or `MoveNew`. All cleanup happens in `finally` and targets only the generated path.

- [ ] **Step 5: Run coordinator and full tests**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveCoordinatorTests
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: PASS; the fake proves no destructive promotion occurs before validation.

- [ ] **Step 6: Commit the coordinator**

```powershell
git add GRF/Core/SafeSave GRF.SafeSave.Tests/SafeSaveCoordinatorTests.cs
git commit -m "feat: add atomic GRF save transaction"
```

### Task 5: Route every GRF/GPF write through safe save

**Files:**
- Modify: `GRF/Core/Container.cs`
- Modify: `GRF/Core/GrfHolder.cs`
- Modify: `GRF/ContainerFormat/ContainerSaveResult.cs`
- Modify: `GRF/ContainerFormat/ContainerExceptions.cs`
- Create: `GRF.SafeSave.Tests/GrfHolderSafeSaveIntegrationTests.cs`

- [ ] **Step 1: Write failing end-to-end tests**

Add tests that create a small source GRF, reopen it, add/replace/rename/delete entries, call `Save`, and assert:

```csharp
Assert.IsTrue(result.Success, result.Error?.ToString());
Assert.IsNotNull(result.SafeSaveReport);
Assert.IsFalse(result.SafeSaveReport.HasErrors);
Assert.IsTrue(File.Exists(path + ".bak"));
Assert.AreEqual(originalHash, Sha256(path + ".bak"));
using (var reopened = new GrfHolder(path)) {
    CollectionAssert.AreEqual(expectedBytes, reopened.FileTable["data\\changed.txt"].GetDecompressedData());
}
```

Add `Cp949_path_round_trips_after_safe_save`: call `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)`, assert `EncodingService.SetDisplayEncoding(949)`, add `data\\sprite\\몬스터\\테스트.txt`, save safely, reopen, and assert the exact path and bytes are present. Restore the previous display encoding in `finally` so tests remain isolated.

Add a protected-format test by changing a new holder header to `GrfStrings.EventHorizon` through the internal test-visible command and asserting `Save().Success == false`, `Error` contains `event-horizon`, and the destination hash is unchanged.

- [ ] **Step 2: Run and observe failure under current direct `FileEdit` behavior**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter GrfHolderSafeSaveIntegrationTests
```

Expected: FAIL because `ContainerSaveResult.SafeSaveReport` does not exist and current quick save does not create `.bak`.

- [ ] **Step 3: Extend the result and add a protected-write exception**

Add to `ContainerSaveResult`:

```csharp
public SafeSaveValidationReport SafeSaveReport { get; set; }
public string BackupFileName { get; set; }
public string TemporaryFileName { get; set; }
```

Add `__SafeSaveFormatReadOnly` to `ContainerExceptions.cs` with message `Writing is blocked for this GRF format: {0}.`.

- [ ] **Step 4: Replace direct saves with the coordinator**

In `GrfHolder.Save`, select `SavingMode.FileCopy` for `.grf` and `.gpf`; retain `Rgz` and `Thor` modes. Change `Merge` from `SavingMode.FileEdit` to `SavingMode.FileCopy`.

In `Container.Save`, classify before setting `IsBusy`; reject non-editable GRF/GPF containers. For file-copy, repack, and compact modes, call a new `_saveSafely` method. `_saveSafely` must:

```csharp
var manifest = SafeSaveManifest.Capture(this);
var destination = fileName ?? FileName;
var outcome = coordinator.Execute(new SafeSaveRequest {
    DestinationPath = destination,
    Options = options ?? new SafeSaveOptions(),
    WriteTemporary = temp => _internalSave(temp, mergeGrf, mode, result),
    ValidateTemporary = temp => new SafeGrfValidator().Validate(temp, manifest)
});
result.SafeSaveReport = outcome.Report;
result.BackupFileName = outcome.BackupPath;
result.TemporaryFileName = outcome.TemporaryPath;
if (!outcome.Success) { result.Fail(outcome.Error); result.RequiresReload = true; return; }
_quickLoad(destination, mode, true);
```

Remove the `File.Delete(sourceFilePath); File.Move(targetFilePath, sourceFilePath);` promotion path. Keep legacy `FileEdit` internal for non-public compatibility but make all `GrfHolder` disk-writing methods avoid it.

- [ ] **Step 5: Run integration and full tests**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter GrfHolderSafeSaveIntegrationTests
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: PASS; backups match the pre-save source and protected writes leave the source hash unchanged.

- [ ] **Step 6: Commit core integration**

```powershell
git add GRF/Core/Container.cs GRF/Core/GrfHolder.cs GRF/ContainerFormat GRF.SafeSave.Tests/GrfHolderSafeSaveIntegrationTests.cs
git commit -m "feat: make GRF writes transactional"
```

### Task 6: Add backup restoration and abandoned-temporary discovery

**Files:**
- Create: `GRF/Core/SafeSave/SafeSaveRecoveryService.cs`
- Create: `GRF.SafeSave.Tests/SafeSaveRecoveryServiceTests.cs`
- Modify: `GRF/Core/GrfHolder.cs`

- [ ] **Step 1: Write failing recovery tests**

Tests must assert that `FindOwnedTemporaries(destination)` returns only files matching `<name>.safe-save-<32 lowercase hex>.tmp`, that an invalid backup is rejected without changing the destination, and that a valid backup restoration leaves the replaced current file at `.restore-point` until final validation succeeds.

- [ ] **Step 2: Run and confirm missing service failure**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveRecoveryServiceTests
```

Expected: FAIL with `CS0246` for `SafeSaveRecoveryService`.

- [ ] **Step 3: Implement recovery with validation before promotion**

`RestoreBackup(destination, backup)` must validate the backup using `SafeGrfValidator`, reject reports with errors, call `File.Replace(backupCopy, destination, destination + ".restore-point", true)`, revalidate destination, and delete `.restore-point` only after success. Work from a temporary copy of `.bak` so successful restoration does not consume the user backup.

Expose from `GrfHolder`:

```csharp
public SafeSaveValidationReport RestoreBackup(string backupPath = null)
```

- [ ] **Step 4: Run recovery and full suites**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveRecoveryServiceTests
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit recovery**

```powershell
git add GRF/Core/SafeSave/SafeSaveRecoveryService.cs GRF/Core/GrfHolder.cs GRF.SafeSave.Tests/SafeSaveRecoveryServiceTests.cs
git commit -m "feat: restore validated GRF backups"
```

### Task 7: Present safe state, reports, settings, and recovery in WPF

**Files:**
- Create: `GRF/Core/SafeSave/SafeSaveUiState.cs`
- Create: `GRF/Core/SafeSave/SafeSaveUiText.cs`
- Create: `GRFEditor/WPF/SafeSaveReportDialog.xaml`
- Create: `GRFEditor/WPF/SafeSaveReportDialog.xaml.cs`
- Create: `GRF.SafeSave.Tests/SafeSaveUiStateTests.cs`
- Modify: `GRFEditor/EditorMainWindow.xaml`
- Modify: `GRFEditor/EditorMainWindow.xaml.cs`
- Modify: `GRFEditor/EMenuInteraction.cs`
- Modify: `GRFEditor/ApplicationConfiguration/GrfEditorConfiguration.cs`
- Modify: `GRFEditor/WPF/SettingsDialog.xaml`
- Modify: `GRFEditor/GRFEditor.csproj`

- [ ] **Step 1: Write failing presentation-state tests**

Test `SafeSaveUiState.From(classification, "pt-BR")` and `From(classification, "en-US")` for these exact labels:

```text
Editável / Editable
Somente leitura — formato protegido / Read-only — protected format
Somente leitura — formato desconhecido / Read-only — unknown format
```

Assert `CanExecuteWrite` is true only for `Editable`.

- [ ] **Step 2: Run and verify missing UI-state types**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveUiStateTests
```

Expected: FAIL with `CS0246` for `SafeSaveUiState`.

- [ ] **Step 3: Implement localized presentation state and preferences**

Add configuration properties:

```csharp
public static bool SafeSaveCreateBackup {
    get => Boolean.Parse(ConfigAsker["[Safe save - Create backup]", true.ToString()]);
    set => ConfigAsker["[Safe save - Create backup]"] = value.ToString();
}
public static bool SafeSaveShowInformation {
    get => Boolean.Parse(ConfigAsker["[Safe save - Show information]", true.ToString()]);
    set => ConfigAsker["[Safe save - Show information]"] = value.ToString();
}
```

Add two checked-by-default checkboxes to the General settings tab: `Create .bak before replacing a GRF` binds to `SafeSaveCreateBackup`, and `Show detailed validation information` binds to `SafeSaveShowInformation`. Validation of every entry remains mandatory in both modes; the second setting changes report verbosity only.

Create `SafeSaveUiText` and `SafeSaveUiState` in the `GRF` project so the `net8.0-windows` test project can test them without referencing the .NET Framework WPF executable. `SafeSaveUiState.From` returns the exact localized labels asserted above, `CanExecuteWrite`, and a localized explanation keyed by `ReasonCode`.

- [ ] **Step 4: Add status, report, and recovery controls**

In `EditorMainWindow.xaml`, use the currently empty row 1 for a compact border containing `_safeSaveStateText`. Rename the File menu save label to `Save safely`; add `_menuItemRestoreBackup` and `_menuItemLastSafeSaveReport`.

In the container-opened event, call:

```csharp
var state = SafeSaveUiState.From(_grfHolder.WriteClassification, CultureInfo.CurrentUICulture.Name);
_safeSaveStateText.Text = state.Label;
_menuItemSave.IsEnabled = state.CanExecuteWrite;
_menuItemSaveAs.IsEnabled = state.CanExecuteWrite;
_menuItemDefragment.IsEnabled = state.CanExecuteWrite;
_menuItemCompact.IsEnabled = state.CanExecuteWrite;
_menuItemRepack.IsEnabled = state.CanExecuteWrite;
_menuItemEncryptTable.IsEnabled = state.CanExecuteWrite;
_menuItemMerge.IsEnabled = state.CanExecuteWrite;
_menuItemSoustract.IsEnabled = state.CanExecuteWrite;
_items.AllowDrop = state.CanExecuteWrite;
_treeView.AllowDrop = state.CanExecuteWrite;
```

Pass `SafeSaveOptions` built from configuration into save calls. Its `PhaseChanged` callback dispatches the localized phase name into `_safeSaveStateText`, giving separate visible write/validate/backup/promote phases while the existing progress bar continues showing numeric writer progress. After completion, assign `_lastSafeSaveReport`, enable the report command, and open `SafeSaveReportDialog` automatically on error. Recovery calls `RestoreBackup`, reloads only after a clean report, and never guesses a nonstandard backup path.

On container open, call `SafeSaveRecoveryService.FindOwnedTemporaries(_grfHolder.FileName)`. When the returned list is non-empty, show paths in `SafeSaveReportDialog` with only `Inspect location` and `Remove selected temporary` actions; removal calls `DeleteOwnedTemporary` for the exact selected owned path and never promotes it.

- [ ] **Step 5: Add WPF project entries and build**

Add explicit `<Compile Include>` entries for the new `.cs` files and `<Page Include>` for the report XAML in `GRFEditor.csproj`.

Run:

```powershell
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" GRFEditor\GRFEditor.csproj /t:Restore,Build /p:Configuration=Debug /p:Platform=AnyCPU
```

Expected: `Build succeeded`, zero errors.

- [ ] **Step 6: Run all automated tests**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit WPF integration**

```powershell
git add GRF/Core/SafeSave/SafeSaveUiState.cs GRF/Core/SafeSave/SafeSaveUiText.cs GRFEditor GRF.SafeSave.Tests/SafeSaveUiStateTests.cs
git commit -m "feat: expose safe save controls"
```

### Task 8: Brand and package the side-by-side application

**Files:**
- Modify: `GRFEditor/ApplicationConfiguration/GrfEditorConfiguration.cs`
- Modify: `GRFEditor/Properties/AssemblyInfo.cs`
- Modify: `GRFEditor/GRFEditor.csproj`
- Modify: `GrfEditor.iss`
- Create: `GRFEditor.Safe.iss`

- [ ] **Step 1: Write a packaging assertion script and observe failure**

Run:

```powershell
$iss = Get-Content -Raw .\GRFEditor.Safe.iss -ErrorAction SilentlyContinue
if ($iss -notmatch 'AppName=GRF Editor Safe' -or
    $iss -notmatch 'DefaultDirName=\{autopf32\}\\GRF Editor Safe' -or
    $iss -match 'ChangesAssociations=yes') { throw 'Side-by-side installer identity is not isolated.' }
```

Expected: FAIL because `GRFEditor.Safe.iss` does not exist.

- [ ] **Step 2: Apply distinct application identity**

Set:

```csharp
public static string ProgramName => "GRF Editor Safe";
```

Set assembly title/product to `GRF Editor Safe`, increment file/assembly version to `1.6.0.0`, and set the WPF output assembly name to `GRF Editor Safe`.

- [ ] **Step 3: Create an isolated installer**

Copy the installer structure into `GRFEditor.Safe.iss` with these exact identity values:

```ini
[Setup]
AppId={{D5229D36-6D89-4E2B-B9F9-5D670E366A13}
AppName=GRF Editor Safe
DefaultDirName={autopf32}\GRF Editor Safe
DefaultGroupName=GRF Editor Safe
UninstallDisplayIcon={app}\GRF Editor Safe.exe
OutputBaseFilename=GRF Editor Safe Installer
ChangesAssociations=no
```

Reference only `GRFEditor\bin\Release\GRF Editor Safe.exe`, its config, and the icon. Do not register `.grf`, `.gpf`, `.rgz`, `.thor`, or `.grfkey`; the existing editor remains the default application.

- [ ] **Step 4: Re-run packaging assertions and release build**

```powershell
$iss = Get-Content -Raw .\GRFEditor.Safe.iss
if ($iss -notmatch 'AppName=GRF Editor Safe' -or
    $iss -notmatch 'DefaultDirName=\{autopf32\}\\GRF Editor Safe' -or
    $iss -match 'ChangesAssociations=yes') { throw 'Side-by-side installer identity is not isolated.' }
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" GRFEditor\GRFEditor.csproj /t:Restore,Rebuild /p:Configuration=Release /p:Platform=AnyCPU
```

Expected: assertions return normally and Release build succeeds.

- [ ] **Step 5: Commit side-by-side packaging**

```powershell
git add GRFEditor GrfEditor.iss GRFEditor.Safe.iss
git commit -m "build: package GRF Editor Safe side by side"
```

### Task 9: Validate copies of real GRFs without touching originals

**Files:**
- Create: `scripts/prepare-grf-safe-fixtures.ps1`
- Create: `GRF.SafeSave.Tests/RealGrfCompatibilityTests.cs`
- Create: `docs/grf-safe-validation.md`

- [ ] **Step 1: Create the copy-and-hash script**

The script accepts `-ClassicSource` and optional `-ProtectedSource`, creates `artifacts/grf-safe-fixtures`, uses `Copy-Item -LiteralPath` only for the classic sample, writes SHA-256 values to `source-hashes.json`, and marks copied fixtures writable. It reads only the first 46 bytes of the protected source and stores the header hex in the JSON; it never copies the 4.63 GB LATAM file.

Default invocation:

```powershell
.\scripts\prepare-grf-safe-fixtures.ps1 `
  -ClassicSource 'C:\Users\juann\Documents\Juan\Programas Rag\astegrf2024\grf\astegrf2024.grf' `
  -ProtectedSource 'C:\Gravity\Ragnarok\data.grf'
```

- [ ] **Step 2: Verify source hashes before any integration write**

Run:

```powershell
$before = Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\juann\Documents\Juan\Programas Rag\astegrf2024\grf\astegrf2024.grf'
.\scripts\prepare-grf-safe-fixtures.ps1 -ClassicSource 'C:\Users\juann\Documents\Juan\Programas Rag\astegrf2024\grf\astegrf2024.grf' -ProtectedSource 'C:\Gravity\Ragnarok\data.grf'
$after = Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\juann\Documents\Juan\Programas Rag\astegrf2024\grf\astegrf2024.grf'
if ($before.Hash -ne $after.Hash) { throw 'Original classic GRF changed during fixture preparation.' }
```

Expected: hashes match.

- [ ] **Step 3: Write and run real-copy integration tests**

`RealGrfCompatibilityTests` must read `GRF_REAL_FIXTURE_DIR`, call `Assert.Inconclusive` when absent, create a second disposable copy per test, add `data\grf_editor_safe_probe.txt`, save, reopen, validate the full manifest, verify `.bak` equals the per-test pre-save copy, and remove only the disposable directory during cleanup.

Run:

```powershell
$env:GRF_REAL_FIXTURE_DIR = (Resolve-Path .\artifacts\grf-safe-fixtures).Path
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter TestCategory=RealGrf
```

Expected: PASS on the copied `astegrf2024.grf`; no path outside `artifacts/grf-safe-fixtures` is opened for writing.

- [ ] **Step 4: Verify both originals after integration tests**

Run:

```powershell
$manifest = Get-Content -Raw .\artifacts\grf-safe-fixtures\source-hashes.json | ConvertFrom-Json
$classic = Get-FileHash -Algorithm SHA256 -LiteralPath $manifest.classic.path
if ($classic.Hash -ne $manifest.classic.sha256) { throw 'Original classic GRF was modified.' }
$latam = Get-Item -LiteralPath 'C:\Gravity\Ragnarok\data.grf'
if ($latam.Length -ne $manifest.protected.length) { throw 'LATAM data.grf size changed.' }
```

Expected: original classic hash and protected file size are unchanged.

- [ ] **Step 5: Document the compatibility result and remaining boundary**

Create `docs/grf-safe-validation.md` listing the tested classic sample hash, operations exercised, build/test commands, `Event Horizon` read-only result, and the statement that client/patcher acceptance still requires launching a private/iRO-compatible client against a disposable GRF copy. Do not claim LATAM write compatibility.

- [ ] **Step 6: Run final verification**

```powershell
& "$env:ProgramFiles\dotnet\dotnet.exe" test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj
& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" GRFEditor\GRFEditor.csproj /t:Restore,Rebuild /p:Configuration=Release /p:Platform=AnyCPU
git status --short
```

Expected: all tests pass, Release build succeeds, and status contains only the intended documentation/script/test changes before commit.

- [ ] **Step 7: Commit real-sample verification**

```powershell
git add scripts/prepare-grf-safe-fixtures.ps1 GRF.SafeSave.Tests/RealGrfCompatibilityTests.cs docs/grf-safe-validation.md
git commit -m "test: verify safe save with classic GRF copy"
```

## Completion criteria

- Every `.grf`/`.gpf` write exposed by `GrfHolder` uses the safe coordinator.
- `Event Horizon`, unknown headers, unsupported versions, and headers with parser errors are rejected before a temporary write.
- No promotion happens until a reopened temporary archive matches its structural rules and expected logical manifest.
- Existing destinations use `File.Replace` and produce a validated `.bak`; restore validates before and after replacement.
- Tests prove validation and simulated promotion failures preserve destination and prior backup.
- The classic real sample is modified only through workspace copies, and its external original hash remains unchanged.
- The LATAM `data.grf` is never copied or written; only its header and size are read.
- The WPF app and installer use the distinct name and directory `GRF Editor Safe` and do not take over file associations.
- Debug and Release builds complete with zero errors, and the full automated suite passes.
