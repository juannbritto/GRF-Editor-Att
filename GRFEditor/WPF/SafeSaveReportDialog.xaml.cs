using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using GRF.Core.SafeSave;
using TokeiLibrary.WPF.Styles;

namespace GRFEditor.WPF {
	public partial class SafeSaveReportDialog : TkWindow {
		private readonly string _destination;
		private readonly SafeSaveRecoveryService _recoveryService = new SafeSaveRecoveryService();

		public SafeSaveReportDialog(SafeSaveValidationReport report)
			: base("Safe-save validation report", "warning.ico") {
			InitializeComponent();
			SafeSaveValidationReport actual = report ?? new SafeSaveValidationReport();
			_summary.Text = actual.HasErrors ? "Validation completed with errors" : "Validation completed successfully";
			_reportText.Text = actual.Items.Count == 0 ? "No validation issues were reported." : actual.ToString();
		}

		public SafeSaveReportDialog(string destination, IEnumerable<string> temporaries)
			: base("Abandoned safe-save temporaries", "warning.ico") {
			InitializeComponent();
			_destination = destination;
			List<string> paths = temporaries.ToList();
			_summary.Text = "Safe-save temporary files were found. They are never promoted automatically.";
			_reportText.Visibility = Visibility.Collapsed;
			_temporaries.Visibility = Visibility.Visible;
			_temporaries.ItemsSource = paths;
			if (paths.Count > 0) _temporaries.SelectedIndex = 0;
			_inspect.Visibility = Visibility.Visible;
			_remove.Visibility = Visibility.Visible;
		}

		private string SelectedPath => _temporaries.SelectedItem as string;

		private void _inspect_Click(object sender, RoutedEventArgs e) {
			if (SelectedPath != null) Utilities.Services.OpeningService.FileOrFolder(Path.GetDirectoryName(SelectedPath));
		}

		private void _remove_Click(object sender, RoutedEventArgs e) {
			if (SelectedPath == null) return;
			_recoveryService.DeleteOwnedTemporary(_destination, SelectedPath);
			_temporaries.ItemsSource = _recoveryService.FindOwnedTemporaries(_destination).ToList();
		}
	}
}
