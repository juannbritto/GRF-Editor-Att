using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class ClassicRefinedThemeContractTests {
		[TestMethod]
		public void App_merges_classic_refined_theme_after_legacy_theme() {
			string root = FindRepositoryRoot();
			XDocument app = XDocument.Load(Path.Combine(root, "GRFEditor", "App.xaml"));
			XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
			string[] sources = app.Descendants(presentation + "ResourceDictionary")
				.Attributes("Source")
				.Select(attribute => attribute.Value)
				.ToArray();

			CollectionAssert.AreEqual(new[] {
				@"WPF\Styles\GRFEditorStyles.xaml",
				@"WPF\Styles\ClassicRefined.xaml"
			}, sources);
		}

		[TestMethod]
		public void Theme_exposes_required_semantic_resources() {
			string themePath = Path.Combine(FindRepositoryRoot(), "GRFEditor", "WPF", "Styles", "ClassicRefined.xaml");
			Assert.IsTrue(File.Exists(themePath), "The classic-refined theme dictionary must exist.");
			string text = File.ReadAllText(themePath);

			foreach (string key in new[] {
				"GrfSurfaceBrush",
				"GrfAccentBrush",
				"GrfTextBrush",
				"GrfPrimaryButton",
				"GrfDialogFooter",
				"GrfSafeStatusBorder"
			}) {
				StringAssert.Contains(text, "x:Key=\"" + key + "\"");
			}
		}

		[TestMethod]
		public void Main_window_preserves_editor_contract_and_adds_balanced_context() {
			string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GRFEditor", "EditorMainWindow.xaml"));

			foreach (string existingName in new[] {
				"_treeView",
				"_items",
				"_tabControlPreview",
				"_progressBarComponent",
				"_textBoxMainSearch",
				"_menuItemSave",
				"_menuItemSaveAs",
				"_menuItemRestoreBackup",
				"_menuItemLastSafeSaveReport"
			}) {
				StringAssert.Contains(text, "Name=\"" + existingName + "\"");
			}

			foreach (string newName in new[] {
				"_safeSaveStateBorder",
				"_safeSaveStateIcon",
				"_archivePathText",
				"_fileCountText"
			}) {
				StringAssert.Contains(text, "x:Name=\"" + newName + "\"");
			}
		}

		[TestMethod]
		public void Runtime_startup_loads_classic_refined_theme_after_base_theme() {
			string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GRFEditor", "App.xaml.cs"));
			int lightTheme = source.IndexOf("StyleLightBlue.xaml", StringComparison.Ordinal);
			int darkTheme = source.IndexOf("StyleDark.xaml", StringComparison.Ordinal);
			int refinedTheme = source.IndexOf("ClassicRefined.xaml", StringComparison.Ordinal);

			Assert.IsTrue(lightTheme >= 0, "The light base theme must be loaded at runtime.");
			Assert.IsTrue(darkTheme >= 0, "The dark base theme must be loaded at runtime.");
			Assert.IsTrue(refinedTheme > lightTheme && refinedTheme > darkTheme,
				"ClassicRefined.xaml must be merged after either selected base theme.");
		}

		[TestMethod]
		public void Theme_switch_keeps_classic_refined_dictionary_last() {
			string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GRFEditor", "WPF", "SettingsDialog.xaml.cs"));

			StringAssert.Contains(source, "ClassicRefined.xaml");
			StringAssert.Contains(source, "dictionaries.Insert(refinedIndex");
		}

		[TestMethod]
		public void Safety_dialogs_use_refined_footer_and_explicit_action_hierarchy() {
			AssertDialogContract(Path.Combine("GRFEditor", "WPF", "SafeSaveReportDialog.xaml"));
			AssertDialogContract(Path.Combine("GRFEditor", "WPF", "SettingsDialog.xaml"));
			AssertDialogContract(Path.Combine("GRFEditor", "Tools", "GrfValidation", "ValidationDialog.xaml"));

			string report = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "GRFEditor", "WPF", "SafeSaveReportDialog.xaml"));
			foreach (string name in new[] { "_summary", "_reportText", "_temporaries", "_inspect", "_remove" })
				StringAssert.Contains(report, "Name=\"" + name + "\"");
			StringAssert.Contains(report, "Style=\"{StaticResource GrfDangerButton}\"");
		}

		private static void AssertDialogContract(string relativePath) {
			string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
			StringAssert.Contains(text, "UseLayoutRounding=\"True\"");
			StringAssert.Contains(text, "Style=\"{StaticResource GrfDialogFooter}\"");
			Assert.IsTrue(text.Contains("Style=\"{StaticResource GrfPrimaryButton}\"") ||
			              text.Contains("Style=\"{StaticResource GrfSecondaryButton}\""),
				"Dialog must contain an explicitly styled primary or secondary action: " + relativePath);
		}

		[TestMethod]
		public void Common_operation_dialogs_use_refined_contract_without_losing_named_controls() {
			string[] dialogs = {
				Path.Combine("GRFEditor", "WPF", "PropertiesDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "SearchDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "MergeDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "SubtractDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "AddFileDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "ViewEncodingDialog.xaml")
			};

			foreach (string dialog in dialogs)
				AssertDialogContract(dialog);

			AssertNamedControls(dialogs[0], "_properties");
			AssertNamedControls(dialogs[1], "_tbPreview");
			AssertNamedControls(dialogs[2], "_pathBrowserOldGrf", "_pathBrowserNewGrf", "_buttonOK", "_buttonCancel");
			AssertNamedControls(dialogs[3], "_pathBrowserGrf1", "_pathBrowserGrf2", "_textBoxOutputName", "_buttonOK", "_buttonCancel");
			AssertNamedControls(dialogs[4], "_treeView", "_textBoxSourceFile", "_textBoxGrfPath", "_buttonOK", "_buttonCancel");
			AssertNamedControls(dialogs[5], "_cbEncodingSource", "_tbSource", "_cbEncodingDest", "_tbDest");
		}

		private static void AssertNamedControls(string relativePath, params string[] names) {
			string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
			foreach (string name in names)
				StringAssert.Contains(text, "Name=\"" + name + "\"");
		}

		[TestMethod]
		public void Specialist_tool_windows_share_refined_chrome_without_renderer_changes() {
			string[] windows = {
				Path.Combine("GRFEditor", "WPF", "EncryptorDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "HashDialog.xaml"),
				Path.Combine("GRFEditor", "WPF", "ImageConverter.xaml"),
				Path.Combine("GRFEditor", "WPF", "MultiProgressWindow.xaml"),
				Path.Combine("GRFEditor", "WPF", "PatcherDialog.xaml"),
				Path.Combine("GRFEditor", "Tools", "GrfShrinker", "GrfShrinkerDialog.xaml"),
				Path.Combine("GRFEditor", "Tools", "Map", "MapEditorWindow.xaml"),
				Path.Combine("GRFEditor", "Tools", "SpriteEditor", "SpriteConverter.xaml"),
				Path.Combine("GRFEditor", "OpenGL", "WPF", "OpenGLDebugDialog.xaml"),
				Path.Combine("GRFEditor", "Tools", "MapExtractor", "MapExtractorDialog.xaml")
			};

			foreach (string relativePath in windows) {
				string text = File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath));
				StringAssert.Contains(text, "UseLayoutRounding=\"True\"");
				StringAssert.Contains(text, "Background=\"{StaticResource GrfCanvasBrush}\"");
				StringAssert.Contains(text, "Foreground=\"{StaticResource GrfTextBrush}\"");
			}
		}

		private static string FindRepositoryRoot() {
			DirectoryInfo current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

			while (current != null) {
				if (File.Exists(Path.Combine(current.FullName, "GRFEditor.sln")) &&
				    Directory.Exists(Path.Combine(current.FullName, "GRFEditor"))) {
					return current.FullName;
				}

				current = current.Parent;
			}

			throw new DirectoryNotFoundException("Could not locate the GRF Editor repository root.");
		}
	}
}
