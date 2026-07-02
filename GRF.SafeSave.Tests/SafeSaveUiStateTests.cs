using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class SafeSaveUiStateTests {
		[TestMethod]
		public void Editable_state_is_localized_and_allows_writes() {
			ContainerWriteClassification classification = ContainerWritePolicy.Classify(GrfStrings.MasterOfMagic, 2, 0, false);

			SafeSaveUiState portuguese = SafeSaveUiState.From(classification, "pt-BR");
			SafeSaveUiState english = SafeSaveUiState.From(classification, "en-US");

			Assert.AreEqual("Editável", portuguese.Label);
			Assert.AreEqual("Editable", english.Label);
			Assert.IsTrue(portuguese.CanExecuteWrite);
			Assert.IsTrue(english.CanExecuteWrite);
		}

		[TestMethod]
		public void Protected_state_is_localized_and_blocks_writes() {
			ContainerWriteClassification classification = ContainerWritePolicy.Classify(GrfStrings.EventHorizon, 3, 0, false);

			SafeSaveUiState portuguese = SafeSaveUiState.From(classification, "pt-BR");
			SafeSaveUiState english = SafeSaveUiState.From(classification, "en-US");

			Assert.AreEqual("Somente leitura — formato protegido", portuguese.Label);
			Assert.AreEqual("Read-only — protected format", english.Label);
			Assert.IsFalse(portuguese.CanExecuteWrite);
			Assert.IsFalse(english.CanExecuteWrite);
		}

		[TestMethod]
		public void Unknown_state_is_localized_and_blocks_writes() {
			ContainerWriteClassification classification = ContainerWritePolicy.Classify("Unknown format", 2, 0, false);

			SafeSaveUiState portuguese = SafeSaveUiState.From(classification, "pt-BR");
			SafeSaveUiState english = SafeSaveUiState.From(classification, "en-US");

			Assert.AreEqual("Somente leitura — formato desconhecido", portuguese.Label);
			Assert.AreEqual("Read-only — unknown format", english.Label);
			Assert.IsFalse(portuguese.CanExecuteWrite);
			Assert.IsFalse(english.CanExecuteWrite);
		}
	}
}
