using GRF.Core;
using GRF.Core.SafeSave;
using GRFEditor.WPF.VisualState;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class SafeSaveStatusPresentationTests {
		[TestMethod]
		public void Protected_archive_maps_to_read_only_danger_state() {
			ContainerWriteClassification classification = ContainerWritePolicy.Classify(GrfStrings.EventHorizon, 3, 0, false);
			SafeSaveUiState uiState = SafeSaveUiState.From(classification, "pt-BR");

			SafeSaveStatusPresentation result = SafeSaveStatusPresentation.From(uiState);

			Assert.AreEqual(SafeSaveStatusKind.ReadOnly, result.Kind);
			Assert.AreEqual("GrfDangerBrush", result.BrushKey);
			Assert.AreEqual(uiState.Label, result.Label);
			Assert.AreEqual(uiState.Explanation, result.Explanation);
			Assert.IsFalse(result.CanExecuteWrite);
		}

		[TestMethod]
		public void Editable_archive_maps_to_success_state() {
			ContainerWriteClassification classification = ContainerWritePolicy.Classify(GrfStrings.MasterOfMagic, 2, 0, false);
			SafeSaveUiState uiState = SafeSaveUiState.From(classification, "en-US");

			SafeSaveStatusPresentation result = SafeSaveStatusPresentation.From(uiState);

			Assert.AreEqual(SafeSaveStatusKind.Editable, result.Kind);
			Assert.AreEqual("GrfSuccessBrush", result.BrushKey);
			Assert.AreEqual(uiState.Label, result.Label);
			Assert.AreEqual(uiState.Explanation, result.Explanation);
			Assert.IsTrue(result.CanExecuteWrite);
		}
	}
}
