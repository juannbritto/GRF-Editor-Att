namespace GRF.Core.SafeSave {
	public static class ContainerWritePolicy {
		public static ContainerWriteClassification Classify(string magic, byte major, byte minor, bool foundErrors) {
			if (magic == GrfStrings.EventHorizon) {
				return new ContainerWriteClassification(ContainerWriteCapability.ReadOnlyProtected, "event-horizon");
			}

			if (magic == GrfStrings.MasterOfMagic && !foundErrors && IsSupportedClassicVersion(major, minor)) {
				return new ContainerWriteClassification(ContainerWriteCapability.Editable, "classic-supported");
			}

			return new ContainerWriteClassification(
				ContainerWriteCapability.ReadOnlyUnknown,
				foundErrors ? "header-errors" : "unknown-format");
		}

		public static ContainerWriteClassification Classify(GrfHeader header) {
			return Classify(header.Magic, header.MajorVersion, header.MinorVersion, header.FoundErrors);
		}

		private static bool IsSupportedClassicVersion(byte major, byte minor) {
			return major == 1 && (minor == 2 || minor == 3) || major == 2 && minor == 0;
		}
	}
}
