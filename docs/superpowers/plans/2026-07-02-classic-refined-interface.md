# Classic Refined Interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the approved classic-refined visual system to the complete GRF Editor Safe interface without changing archive read/write behavior.

**Architecture:** Add one application-owned WPF resource dictionary after the legacy Tokei style dictionary, then update the main shell and dialogs to consume semantic styles. Keep event handlers, control names, safe-save services, and all GRF serialization code unchanged; add source-level XAML contract tests and preserve the existing compatibility suite.

**Tech Stack:** C# 7/.NET Framework 4.8, WPF XAML, MSTest, MSBuild, PowerShell, existing TokeiLibrary controls.

---

## File Map

- Create `GRFEditor/WPF/Styles/ClassicRefined.xaml`: application palette, spacing, typography, and reusable WPF styles.
- Modify `GRFEditor/App.xaml`: merge the new dictionary after legacy styles.
- Modify `GRFEditor/GRFEditor.csproj`: register the new WPF page and test-support source where required.
- Modify `GRFEditor/EditorMainWindow.xaml`: balanced A2 shell, action bar, pane headings, and status strip.
- Modify `GRFEditor/EditorMainWindow.xaml.cs`: map existing safe-save UI state to presentation-only visual states.
- Create `GRFEditor/WPF/VisualState/SafeSaveStatusPresentation.cs`: pure mapping from safe-save capability/phase to semantic visual state.
- Modify high-use dialogs: `SafeSaveReportDialog.xaml`, `SettingsDialog.xaml`, `Tools/GrfValidation/ValidationDialog.xaml`, `PropertiesDialog.xaml`, `SearchDialog.xaml`, `MergeDialog.xaml`, and `SubtractDialog.xaml`.
- Modify remaining application-owned windows under `GRFEditor/WPF`, `GRFEditor/Tools`, and `GRFEditor/OpenGL/WPF`: shared window, input, button, tab, and footer styles only.
- Create `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`: XAML merge/resource/control-contract checks.
- Create `GRF.SafeSave.Tests/SafeSaveStatusPresentationTests.cs`: presentation mapping tests.
- Modify `README.md`: describe the visual update and its archive-safety boundary.

### Task 1: Add the theme contract test and resource dictionary

**Files:**
- Create: `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`
- Create: `GRFEditor/WPF/Styles/ClassicRefined.xaml`
- Modify: `GRFEditor/App.xaml`
- Modify: `GRFEditor/GRFEditor.csproj`

- [ ] **Step 1: Write the failing XAML contract test**

Add a test that walks from `AppDomain.CurrentDomain.BaseDirectory` to the repository root, loads the two XAML files with `XDocument`, and asserts the new dictionary is merged after `GRFEditorStyles.xaml` and contains the required keys:

```csharp
[TestMethod]
public void App_merges_classic_refined_theme_after_legacy_theme() {
    string root = FindRepositoryRoot();
    XDocument app = XDocument.Load(Path.Combine(root, "GRFEditor", "App.xaml"));
    XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    string[] sources = app.Descendants(p + "ResourceDictionary")
        .Attributes("Source").Select(a => a.Value).ToArray();

    CollectionAssert.AreEqual(new[] {
        @"WPF\Styles\GRFEditorStyles.xaml",
        @"WPF\Styles\ClassicRefined.xaml"
    }, sources);
}

[TestMethod]
public void Theme_exposes_required_semantic_resources() {
    string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
        "GRFEditor", "WPF", "Styles", "ClassicRefined.xaml"));
    foreach (string key in new[] { "GrfSurfaceBrush", "GrfAccentBrush", "GrfTextBrush",
             "GrfPrimaryButton", "GrfDialogFooter", "GrfSafeStatusBorder" })
        StringAssert.Contains(text, "x:Key=\"" + key + "\"");
}
```

Implement `FindRepositoryRoot()` by walking parents until both `GRFEditor.sln` and `GRFEditor` exist; fail with `DirectoryNotFoundException` if not found.

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ClassicRefinedThemeContractTests
```

Expected: FAIL because `ClassicRefined.xaml` is absent and the second merged dictionary is missing.

- [ ] **Step 3: Implement the semantic resource dictionary**

Create a valid WPF `ResourceDictionary` with semantic brushes and keyed styles. Start with these stable resource values:

```xml
<SolidColorBrush x:Key="GrfCanvasBrush" Color="#FFF3F5F7" />
<SolidColorBrush x:Key="GrfSurfaceBrush" Color="#FFFFFFFF" />
<SolidColorBrush x:Key="GrfBorderBrush" Color="#FFD6DBE1" />
<SolidColorBrush x:Key="GrfTextBrush" Color="#FF20262E" />
<SolidColorBrush x:Key="GrfMutedTextBrush" Color="#FF626D78" />
<SolidColorBrush x:Key="GrfAccentBrush" Color="#FF2868A9" />
<SolidColorBrush x:Key="GrfSuccessBrush" Color="#FF287A4B" />
<SolidColorBrush x:Key="GrfWarningBrush" Color="#FF9A6500" />
<SolidColorBrush x:Key="GrfDangerBrush" Color="#FFB43A3A" />
```

Define `GrfPrimaryButton`, `GrfSecondaryButton`, `GrfDangerButton`, `GrfPaneHeader`, `GrfDialogFooter`, and `GrfSafeStatusBorder`. Styles must include visible keyboard focus, disabled opacity no lower than `0.55`, and minimum control heights of 28 pixels.

Merge the dictionary in `App.xaml` after `GRFEditorStyles.xaml` and add:

```xml
<Page Include="WPF\Styles\ClassicRefined.xaml">
  <Generator>MSBuild:Compile</Generator>
  <SubType>Designer</SubType>
</Page>
```

to `GRFEditor.csproj` alongside other XAML pages.

- [ ] **Step 4: Run tests and build**

Run:

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ClassicRefinedThemeContractTests
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" GRFEditor.sln /t:Build /p:Configuration=Debug /p:Platform=x86
```

Expected: contract tests PASS and solution build succeeds with 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add GRFEditor/App.xaml GRFEditor/GRFEditor.csproj GRFEditor/WPF/Styles/ClassicRefined.xaml GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs
git commit -m "feat: add classic refined visual system"
```

### Task 2: Add a testable safe-save presentation adapter

**Files:**
- Create: `GRFEditor/WPF/VisualState/SafeSaveStatusPresentation.cs`
- Create: `GRF.SafeSave.Tests/SafeSaveStatusPresentationTests.cs`
- Modify: `GRFEditor/GRFEditor.csproj`
- Modify: `GRF.SafeSave.Tests/GRF.SafeSave.Tests.csproj`

- [ ] **Step 1: Write failing mapping tests**

Link the new source into the test project and test the presentation-only mapping:

```csharp
[TestMethod]
public void Protected_archive_maps_to_read_only_danger_state() {
    var classification = ContainerWritePolicy.Classify(GrfStrings.EventHorizon, 3, 0, false);
    var ui = SafeSaveUiState.From(classification, "pt-BR");
    SafeSaveStatusPresentation result = SafeSaveStatusPresentation.From(ui);

    Assert.AreEqual(SafeSaveStatusKind.ReadOnly, result.Kind);
    Assert.AreEqual("GrfDangerBrush", result.BrushKey);
    Assert.IsFalse(result.CanExecuteWrite);
}

[TestMethod]
public void Editable_archive_maps_to_success_state() {
    var classification = ContainerWritePolicy.Classify(GrfStrings.MasterOfMagic, 2, 0, false);
    var result = SafeSaveStatusPresentation.From(SafeSaveUiState.From(classification, "en-US"));

    Assert.AreEqual(SafeSaveStatusKind.Editable, result.Kind);
    Assert.AreEqual("GrfSuccessBrush", result.BrushKey);
    Assert.IsTrue(result.CanExecuteWrite);
}
```

- [ ] **Step 2: Run the tests and verify failure**

Run:

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter SafeSaveStatusPresentationTests
```

Expected: FAIL because `SafeSaveStatusPresentation` is undefined.

- [ ] **Step 3: Implement the immutable adapter**

Define `SafeSaveStatusKind` values `Waiting`, `Editable`, `Busy`, `Warning`, and `ReadOnly`. `From(SafeSaveUiState state)` must preserve `Label`, `Explanation`, and `CanExecuteWrite`; choose brush keys only from the semantic theme. Do not import or call archive writing services.

Link the file in the SDK-style test project:

```xml
<Compile Include="..\GRFEditor\WPF\VisualState\SafeSaveStatusPresentation.cs" Link="SafeSaveStatusPresentation.cs" />
```

- [ ] **Step 4: Run mapping and existing state tests**

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter "SafeSaveStatusPresentationTests|SafeSaveUiStateTests"
```

Expected: all selected tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add GRFEditor/WPF/VisualState/SafeSaveStatusPresentation.cs GRFEditor/GRFEditor.csproj GRF.SafeSave.Tests/SafeSaveStatusPresentationTests.cs GRF.SafeSave.Tests/GRF.SafeSave.Tests.csproj
git commit -m "feat: map safe save state to visual status"
```

### Task 3: Recompose the main window as the balanced A2 layout

**Files:**
- Modify: `GRFEditor/EditorMainWindow.xaml`
- Modify: `GRFEditor/EditorMainWindow.xaml.cs`
- Modify: `GRFEditor/EMenuInteraction.cs`
- Modify: `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`

- [ ] **Step 1: Extend the source contract test**

Assert that `EditorMainWindow.xaml` retains `_treeView`, `_items`, `_tabControlPreview`, `_progressBarComponent`, `_textBoxMainSearch`, and all existing safe-save menu item names, while adding `_safeSaveStateBorder`, `_safeSaveStateIcon`, `_archivePathText`, and `_fileCountText`.

- [ ] **Step 2: Run the focused test and verify failure**

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ClassicRefinedThemeContractTests
```

Expected: FAIL for the four new named elements.

- [ ] **Step 3: Implement the A2 shell**

Keep the three-pane grid and persisted splitters. Recompose only the shell:

- 30-pixel menu row;
- 40-pixel action row with text-labeled `Open` and `Save safely` primary actions plus undo/redo/navigation icons;
- 32-pixel contextual row for archive path, global search, progress, and safe-save state;
- pane headers above tree, file list, and preview;
- 28-pixel bottom status row for selection/file counts and operation feedback.

Use `Style="{StaticResource GrfPrimaryButton}"` for safe save, `GrfSecondaryButton` for routine actions, and keep compact/defragment/repack/encryption inside existing advanced menus. Preserve every click handler and keyboard shortcut.

In `_updateSafeSaveState()`, create the presentation value and resolve its brush with `TryFindResource`:

```csharp
SafeSaveStatusPresentation visual = SafeSaveStatusPresentation.From(state);
_safeSaveStateText.Text = visual.Label;
_safeSaveStateText.ToolTip = visual.Explanation;
_safeSaveStateBorder.BorderBrush = TryFindResource(visual.BrushKey) as Brush;
_safeSaveStateIcon.Text = visual.Kind == SafeSaveStatusKind.ReadOnly ? "!" : "✓";
```

When a save phase is reported in `EMenuInteraction.cs`, update text through the dispatcher but do not alter the coordinator callbacks or their order.

- [ ] **Step 4: Build and manually smoke-test the empty shell**

```powershell
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" GRFEditor.sln /t:Build /p:Configuration=Debug /p:Platform=x86
```

Expected: 0 build errors. Launch Debug, verify empty state, menu shortcuts, pane splitters, search boxes, and no missing-resource exception.

- [ ] **Step 5: Commit**

```powershell
git add GRFEditor/EditorMainWindow.xaml GRFEditor/EditorMainWindow.xaml.cs GRFEditor/EMenuInteraction.cs GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs
git commit -m "feat: refresh main editor workspace"
```

### Task 4: Modernize safe-save, validation, and settings dialogs

**Files:**
- Modify: `GRFEditor/WPF/SafeSaveReportDialog.xaml`
- Modify: `GRFEditor/WPF/SettingsDialog.xaml`
- Modify: `GRFEditor/Tools/GrfValidation/ValidationDialog.xaml`
- Modify: `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`

- [ ] **Step 1: Add dialog contract assertions**

For each file, assert `Style="{StaticResource GrfDialogFooter}"` is present, the root has `UseLayoutRounding="True"`, and buttons use primary, secondary, or danger styles. Assert the report dialog retains `_summary`, `_reportText`, `_temporaries`, `_inspect`, and `_remove`.

- [ ] **Step 2: Verify the new assertions fail**

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ClassicRefinedThemeContractTests
```

Expected: FAIL because dialog footer and button styles are not yet applied.

- [ ] **Step 3: Apply the shared dialog composition**

Use a consistent 16-pixel outer margin, section heading, scrollable body, and 52-pixel footer. In `SafeSaveReportDialog`, style `_remove` as danger, `_inspect` as secondary, and Close as primary. In Settings, remove the fixed `MaxHeight="560"`, set `MinWidth="560"`, `MinHeight="600"`, and place tab content in scroll viewers so 125–200% scaling does not clip controls. In Validation, retain all checkbox bindings and result controls while replacing decorative rectangles and `FancyButton` callouts with shared heading/action styles.

- [ ] **Step 4: Build and inspect representative states**

Build Debug, then inspect: report with errors; owned-temporary recovery; settings at each tab; validation options/results; 100%, 150%, and 200% scaling. Expected: no clipped actions and no altered bindings.

- [ ] **Step 5: Commit**

```powershell
git add GRFEditor/WPF/SafeSaveReportDialog.xaml GRFEditor/WPF/SettingsDialog.xaml GRFEditor/Tools/GrfValidation/ValidationDialog.xaml GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs
git commit -m "feat: unify safety and settings dialogs"
```

### Task 5: Apply shared styling to common operation dialogs

**Files:**
- Modify: `GRFEditor/WPF/PropertiesDialog.xaml`
- Modify: `GRFEditor/WPF/SearchDialog.xaml`
- Modify: `GRFEditor/WPF/MergeDialog.xaml`
- Modify: `GRFEditor/WPF/SubtractDialog.xaml`
- Modify: `GRFEditor/WPF/AddFileDialog.xaml`
- Modify: `GRFEditor/WPF/ViewEncodingDialog.xaml`
- Modify: `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`

- [ ] **Step 1: Add named-control preservation tests**

Record every `x:Name`/`Name` in these six files and add assertions for controls referenced by their code-behind. Also assert each dialog uses `GrfDialogFooter` and at least one shared button style.

- [ ] **Step 2: Verify tests fail before styling**

Run the focused theme contract tests. Expected: style assertions FAIL; named-control assertions PASS.

- [ ] **Step 3: Recompose the dialogs without changing handlers**

Apply shared headings, grouped fields, minimum 28-pixel inputs, 8-pixel control gaps, and standard footers. Merge/subtract confirmations use clear source/destination labels. Any irreversible operation uses `GrfDangerButton`; cancel/close uses secondary styling. Preserve all bindings, event handler names, dialog results, and defaults.

- [ ] **Step 4: Build and inspect keyboard behavior**

Verify Enter activates only the intended default, Escape closes cancellable dialogs, Tab order is logical, labels do not clip at 150%, and long archive paths wrap or ellipsize with a tooltip.

- [ ] **Step 5: Commit**

```powershell
git add GRFEditor/WPF/PropertiesDialog.xaml GRFEditor/WPF/SearchDialog.xaml GRFEditor/WPF/MergeDialog.xaml GRFEditor/WPF/SubtractDialog.xaml GRFEditor/WPF/AddFileDialog.xaml GRFEditor/WPF/ViewEncodingDialog.xaml GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs
git commit -m "feat: refresh common archive dialogs"
```

### Task 6: Cover remaining application-owned windows

**Files:**
- Modify: application-owned `.xaml` files under `GRFEditor/WPF`, `GRFEditor/Tools`, and `GRFEditor/OpenGL/WPF`
- Exclude from functional redesign: preview/render controls under `GRFEditor/WPF/PreviewTabs` and specialist renderer internals
- Modify: `GRF.SafeSave.Tests/ClassicRefinedThemeContractTests.cs`

- [ ] **Step 1: Add a coverage inventory test**

Enumerate XAML roots that are `Window` or `Styles:TkWindow`. Assert each opts into `UseLayoutRounding="True"` and contains no hard-coded footer background where `GrfDialogFooter` applies. Keep an explicit allowlist for test windows and embedded user controls.

- [ ] **Step 2: Run and capture the failing file list**

Run the contract test and use its failure output as the migration checklist. Expected: multiple windows fail before migration.

- [ ] **Step 3: Apply only shared chrome and spacing**

Update window backgrounds, typography, inputs, buttons, tabs, section headers, and footers. Do not change OpenGL, sprite, map, audio, image, raw-data, or hex rendering code. Do not rename controls or move event handlers.

- [ ] **Step 4: Re-run the inventory until clean and build Release**

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --filter ClassicRefinedThemeContractTests
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" GRFEditor.sln /t:Build /p:Configuration=Release /p:Platform=x86
```

Expected: tests PASS and 0 build errors.

- [ ] **Step 5: Commit**

Stage only reviewed XAML and the contract test, then:

```powershell
git commit -m "feat: apply refined styling across editor tools"
```

### Task 7: Run full archive-safety and visual verification

**Files:**
- Modify: `docs/grf-safe-validation.md`
- Modify: `README.md`

- [ ] **Step 1: Run the complete automated suite**

```powershell
dotnet test GRF.SafeSave.Tests\GRF.SafeSave.Tests.csproj --configuration Release
```

Expected: all pre-existing tests plus visual contract tests PASS; no existing test is removed or weakened.

- [ ] **Step 2: Re-run real classic-GRF compatibility on a copy**

Use `scripts/prepare-grf-safe-fixtures.ps1` with the original `astegrf2024.grf`, then run `RealGrfCompatibilityTests`. Record the original SHA-256 before and after. Expected: hashes match exactly; the working copy validates and reopens successfully.

- [ ] **Step 3: Verify protected-format behavior**

Open only the Event Horizon header/classification fixture. Expected: clear read-only danger state and disabled save commands. Do not copy, rewrite, or repack the installed LATAM archive.

- [ ] **Step 4: Complete the visual matrix**

Inspect the main window plus Settings, Validation, Safe-save Report, Properties, Search, Merge, and Subtract at 100%, 125%, 150%, and 200%. Verify empty/loaded/busy/success/warning/read-only states, keyboard focus, long paths, minimum sizes, and splitters. Log results in `docs/grf-safe-validation.md`.

- [ ] **Step 5: Update project documentation**

Add a README section describing classic-refined A2, full-interface coverage, accessibility/DPI improvements, unchanged GRF serialization, copy-first compatibility verification, and side-by-side installation.

- [ ] **Step 6: Commit**

```powershell
git add README.md docs/grf-safe-validation.md
git commit -m "docs: record refined interface validation"
```

### Task 8: Package, verify, and publish the side-by-side release

**Files:**
- Modify: `GRFEditor/Properties/AssemblyInfo.cs`
- Modify: `GRFEditor.Safe.iss` only if packaged file metadata needs updating

- [ ] **Step 1: Build the final Release executable**

Build Release x86 and confirm the output remains `GRF Editor Safe.exe`, not `GRF Editor.exe`.

- [ ] **Step 2: Check the working tree for build artifacts**

```powershell
git status --short
```

Restore only build-generated tracked DLL changes after confirming they are generated outputs; never discard source changes.

- [ ] **Step 3: Final smoke test**

Launch the packaged executable side by side with the original editor. Open a copied classic GRF, navigate/search/preview, perform safe save, inspect the validation report, close/reopen the copy, and verify the original hash remains unchanged.

- [ ] **Step 4: Commit release metadata**

```powershell
git add GRFEditor/Properties/AssemblyInfo.cs GRFEditor.Safe.iss
git commit -m "build: package classic refined interface"
```

- [ ] **Step 5: Push the complete series**

```powershell
git push origin main
```

Expected: GitHub `main` contains the theme, application changes, tests, documentation, and release metadata with a clean local working tree.
