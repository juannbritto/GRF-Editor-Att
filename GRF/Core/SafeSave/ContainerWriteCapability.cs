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
