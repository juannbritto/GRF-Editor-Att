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
