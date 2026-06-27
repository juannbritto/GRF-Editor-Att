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
