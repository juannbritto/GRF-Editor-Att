using GRF.Core.SafeSave;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GRF.SafeSave.Tests {
	[TestClass]
	public class ContainerWritePolicyTests {
		[TestMethod]
		public void Classic_supported_header_is_editable() {
			var classification = ContainerWritePolicy.Classify("Master of Magic\0", 2, 0, false);

			Assert.AreEqual(ContainerWriteCapability.Editable, classification.Capability);
			Assert.AreEqual("classic-supported", classification.ReasonCode);
			Assert.IsTrue(classification.CanWrite);
		}

		[TestMethod]
		public void Event_horizon_is_protected_even_when_version_is_supported() {
			var classification = ContainerWritePolicy.Classify("Event Horizon\0RL", 3, 0, false);

			Assert.AreEqual(ContainerWriteCapability.ReadOnlyProtected, classification.Capability);
			Assert.AreEqual("event-horizon", classification.ReasonCode);
			Assert.IsFalse(classification.CanWrite);
		}

		[TestMethod]
		public void Unknown_magic_or_header_errors_are_read_only() {
			var unknownMagic = ContainerWritePolicy.Classify("Unknown format", 2, 0, false);
			var headerErrors = ContainerWritePolicy.Classify("Master of Magic\0", 2, 0, true);

			Assert.AreEqual(ContainerWriteCapability.ReadOnlyUnknown, unknownMagic.Capability);
			Assert.AreEqual("unknown-format", unknownMagic.ReasonCode);
			Assert.AreEqual(ContainerWriteCapability.ReadOnlyUnknown, headerErrors.Capability);
			Assert.AreEqual("header-errors", headerErrors.ReasonCode);
		}
	}
}
