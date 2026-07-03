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
