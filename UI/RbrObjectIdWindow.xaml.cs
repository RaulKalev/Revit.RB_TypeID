using Microsoft.Win32;
using RB_TypeName.Handlers;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Autodesk.Revit.UI;

namespace RB_TypeName.UI
{
    public partial class RbrObjectIdWindow : Window
    {
        private static readonly string SettingsFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RK Tools", "RB_TypeName");

        private static readonly string SettingsFile =
            Path.Combine(SettingsFolder, "settings.json");

        private readonly AssignRbrObjectIdsHandler _handler;
        private readonly ExternalEvent             _externalEvent;

        private PbsExcelSourceSettings _settings;

        // Constructor

        public RbrObjectIdWindow(AssignRbrObjectIdsHandler handler, ExternalEvent externalEvent)
        {
            InitializeComponent();
            _handler       = handler;
            _externalEvent = externalEvent;

            _handler.OnCompleted = (results, index) =>
                Dispatcher.Invoke(() => ShowResults(results, index));

            LoadSettings();
        }

        // Settings persistence

        private void LoadSettings()
        {
            _settings = PbsExcelSourceSettings.LoadFromFile(SettingsFile);

            if (!string.IsNullOrEmpty(_settings.ExcelPath))
            {
                PbsFilePathBox.Text = _settings.ExcelPath;

                if (File.Exists(_settings.ExcelPath))
                    ApplyLoadResult(PbsMappingService.Load(_settings));
                else
                    SetStatus("PBS file not found: " + _settings.ExcelPath, isError: true);
            }
        }

        private void SaveSettings()
        {
            try { Directory.CreateDirectory(SettingsFolder); } catch { }
            _settings.SaveToFile(SettingsFile);
        }

        // PBS File

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select PBS Excel file",
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            };

            if (!string.IsNullOrEmpty(_settings.ExcelPath) && File.Exists(_settings.ExcelPath))
                dlg.InitialDirectory = Path.GetDirectoryName(_settings.ExcelPath);

            if (dlg.ShowDialog() != true) return;

            _settings.ExcelPath = dlg.FileName;
            PbsFilePathBox.Text = dlg.FileName;
            SaveSettings();

            ApplyLoadResult(PbsMappingService.Load(_settings));
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_settings?.ExcelPath))
            {
                SetStatus("No PBS file selected.", isError: true);
                return;
            }

            string previousCode = (MappingCombo.SelectedItem as PbsMapping)?.PbsCode;
            ApplyLoadResult(PbsMappingService.Load(_settings), restoreCode: previousCode);
        }

        private void ApplyLoadResult(PbsLoadResult result, string restoreCode = null)
        {
            MappingCombo.ItemsSource = null;

            if (!result.Success)
            {
                SetStatus(result.ErrorMessage, isError: true);
                AssignButton.IsEnabled = false;
                return;
            }

            MappingCombo.ItemsSource = result.Mappings;

            int idx = restoreCode != null
                ? result.Mappings.FindIndex(m =>
                    string.Equals(m.PbsCode, restoreCode, StringComparison.OrdinalIgnoreCase))
                : -1;

            MappingCombo.SelectedIndex = idx >= 0 ? idx : 0;

            SetStatus(result.Mappings.Count + " mapping(s) loaded.", isError: false);
            AssignButton.IsEnabled = true;
        }

        private void SetStatus(string text, bool isError)
        {
            MappingStatusText.Text       = text;
            MappingStatusText.Foreground = isError
                ? System.Windows.Media.Brushes.OrangeRed
                : System.Windows.Media.Brushes.Gray;
        }

        // Assign

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (MappingCombo.SelectedItem is not PbsMapping mapping)
            {
                MessageBox.Show(
                    "Please load the PBS file and select a mapping first.",
                    "RBR Object ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(_settings?.ExcelPath ?? string.Empty))
            {
                MessageBox.Show(
                    "PBS Excel file was not found:\n" + (_settings?.ExcelPath ?? "(none)") +
                    "\n\nPlease select a valid file before assigning IDs.",
                    "RBR Object ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _handler.SelectedMapping  = mapping;
            AssignButton.IsEnabled    = false;
            SummaryText.Text          = "Running...";
            ResultsGrid.ItemsSource   = null;
            WarningsBorder.Visibility = Visibility.Collapsed;

            _externalEvent.Raise();
        }

        // Results

        private void ShowResults(List<RbrIdAssignmentResult> results, RbrObjectIdIndex index)
        {
            AssignButton.IsEnabled  = true;
            ResultsGrid.ItemsSource = results;

            if (results == null || results.Count == 0)
            {
                SummaryText.Text = "No results returned.";
                return;
            }

            int assigned = results.Count(r => r.Status == "Assigned");
            int skipped  = results.Count(r => r.Status == "SkippedExisting");
            int issues   = results.Count(r => r.Status == "Error"
                                           || r.Status == "MissingLevel"
                                           || r.Status == "MissingParameter"
                                           || r.Status == "ReadOnly");

            SummaryText.Text = "Assigned: " + assigned
                + "   |   Skipped (existing): " + skipped
                + "   |   Issues: " + issues;

            if (index != null && (index.Duplicates.Count > 0 || index.Malformed.Count > 0))
            {
                var sb = new System.Text.StringBuilder();
                if (index.Duplicates.Count > 0)
                    sb.AppendLine("Duplicate IDs found in model (" + index.Duplicates.Count + "): "
                        + string.Join(", ", index.Duplicates.Take(5))
                        + (index.Duplicates.Count > 5 ? " ..." : ""));
                if (index.Malformed.Count > 0)
                    sb.AppendLine("Malformed IDs found in model (" + index.Malformed.Count + "): "
                        + string.Join(", ", index.Malformed.Take(5))
                        + (index.Malformed.Count > 5 ? " ..." : ""));

                WarningsText.Text         = sb.ToString().Trim();
                WarningsBorder.Visibility = Visibility.Visible;
            }
        }
    }
}