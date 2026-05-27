using Microsoft.Win32;
using RB_TypeName.Handlers;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        // ── Handlers / events ────────────────────────────────────────────────

        private readonly AssignRbrObjectIdsHandler  _assignHandler;
        private readonly ExternalEvent              _assignEvent;

        private readonly PreviewRbrObjectIdsHandler _previewHandler;
        private readonly ExternalEvent              _previewEvent;

        private readonly ApplyRbrObjectIdsHandler   _applyHandler;
        private readonly ExternalEvent              _applyEvent;

        // ── State ────────────────────────────────────────────────────────────

        private PbsExcelSourceSettings  _settings;
        private List<ObjectIdPreviewRow> _previewRows;

        // ── Constructor ──────────────────────────────────────────────────────

        public RbrObjectIdWindow(
            AssignRbrObjectIdsHandler  assignHandler,  ExternalEvent assignEvent,
            PreviewRbrObjectIdsHandler previewHandler, ExternalEvent previewEvent,
            ApplyRbrObjectIdsHandler   applyHandler,   ExternalEvent applyEvent)
        {
            InitializeComponent();

            _assignHandler  = assignHandler;
            _assignEvent    = assignEvent;
            _previewHandler = previewHandler;
            _previewEvent   = previewEvent;
            _applyHandler   = applyHandler;
            _applyEvent     = applyEvent;

            _assignHandler.OnCompleted = (results, index) =>
                Dispatcher.Invoke(() => ShowAssignResults(results, index));

            _previewHandler.OnCompleted = (rows, index) =>
                Dispatcher.Invoke(() => ShowPreviewResults(rows, index));

            _applyHandler.OnCompleted = (assigned, skipped, errors) =>
                Dispatcher.Invoke(() => ShowApplyResults(assigned, skipped, errors));

            LoadSettings();
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            _settings = PbsExcelSourceSettings.LoadFromFile(SettingsFile);

            UseRbrPrCodeLookupCheck.IsChecked       = _settings.UseRbrPrCodeLookup;
            UseManualMappingFallbackCheck.IsChecked = _settings.UseManualMappingFallback;

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
            _settings.UseRbrPrCodeLookup      = UseRbrPrCodeLookupCheck.IsChecked == true;
            _settings.UseManualMappingFallback = UseManualMappingFallbackCheck.IsChecked == true;

            try { Directory.CreateDirectory(SettingsFolder); } catch { }
            _settings.SaveToFile(SettingsFile);
        }

        // ── PBS File ─────────────────────────────────────────────────────────

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
            ResetPreviewState();
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
            ResetPreviewState();
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
                ? Brushes.OrangeRed
                : Brushes.Gray;
        }

        // ── Preview ──────────────────────────────────────────────────────────

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_settings?.ExcelPath ?? string.Empty))
            {
                MessageBox.Show(
                    "PBS Excel file not found.\nPlease select a valid file first.",
                    "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (success, error, prCodeRows) = PbsMappingService.LoadPrCodeRows(_settings);
            if (!success)
            {
                MessageBox.Show(error, "PBS Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _previewHandler.LookupService = new PbsPrCodeLookupService(prCodeRows);

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text           = "Building preview…";
            ResultsGrid.ItemsSource    = null;
            WarningsBorder.Visibility  = Visibility.Collapsed;
            _previewRows               = null;

            _previewEvent.Raise();
        }

        private void ShowPreviewResults(List<ObjectIdPreviewRow> rows, RbrObjectIdIndex index)
        {
            _previewRows            = rows;
            ResultsGrid.ItemsSource = rows;

            if (rows == null || rows.Count == 0)
            {
                SummaryText.Text = "No elements in current Revit selection.";
                SetButtons(previewEnabled: true, applyEnabled: false, assignEnabled: true, exportEnabled: false);
                return;
            }

            int ready    = rows.Count(r => r.Status == "Ready");
            int existing = rows.Count(r => r.Status == "Already has Object ID");
            int issues   = rows.Count(r => r.Status != "Ready" && r.Status != "Already has Object ID");

            SummaryText.Text = $"Ready: {ready}   |   Already set: {existing}   |   Issues: {issues}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   ready > 0,
                assignEnabled:  true,
                exportEnabled:  rows.Count > 0);

            ShowIndexWarnings(index);
        }

        // ── Apply ────────────────────────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_previewRows == null || _previewRows.Count == 0)
            {
                MessageBox.Show("Please run Preview first.", "Apply",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toApply = _previewRows
                .Where(r => r.IsSelected && r.IsReady)
                .ToList();

            if (toApply.Count == 0)
            {
                MessageBox.Show("No Ready rows are selected to apply.", "Apply",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _applyHandler.RowsToApply = toApply;

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text          = "Applying…";
            WarningsBorder.Visibility = Visibility.Collapsed;

            _applyEvent.Raise();
        }

        private void ShowApplyResults(int assigned, int skipped, List<string> errors)
        {
            // Refresh grid to show updated statuses (status objects were mutated in-place).
            var rows = _previewRows;
            ResultsGrid.ItemsSource = null;
            ResultsGrid.ItemsSource = rows;

            SummaryText.Text = $"Assigned: {assigned}   |   Skipped: {skipped}   |   Errors: {errors.Count}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   false,   // Prevent double-apply; run Preview again if needed.
                assignEnabled:  true,
                exportEnabled:  rows != null && rows.Count > 0);

            if (errors.Count > 0)
            {
                WarningsText.Text         = string.Join("\n", errors);
                WarningsBorder.Visibility = Visibility.Visible;
            }
        }

        // ── Assign (manual fallback) ─────────────────────────────────────────

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

            _assignHandler.SelectedMapping = mapping;

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text          = "Running manual assignment…";
            ResultsGrid.ItemsSource   = null;
            WarningsBorder.Visibility = Visibility.Collapsed;
            _previewRows              = null;

            _assignEvent.Raise();
        }

        private void ShowAssignResults(List<RbrIdAssignmentResult> results, RbrObjectIdIndex index)
        {
            // Convert RbrIdAssignmentResult → ObjectIdPreviewRow for the shared grid.
            var rows = results?.Select(r => new ObjectIdPreviewRow
            {
                ElementId        = r.ElementId,
                ExistingObjectId = r.OldValue ?? string.Empty,
                ProposedObjectId = r.NewValue ?? string.Empty,
                Status           = MapAssignStatus(r.Status),
                Message          = r.Message ?? string.Empty,
                IsSelected       = false, // Already applied — nothing to select.
            }).ToList() ?? new List<ObjectIdPreviewRow>();

            _previewRows            = rows;
            ResultsGrid.ItemsSource = rows;

            int assigned = results?.Count(r => r.Status == "Assigned")     ?? 0;
            int skipped  = results?.Count(r => r.Status == "SkippedExisting") ?? 0;
            int issues   = results?.Count(r => r.Status != "Assigned"
                                            && r.Status != "SkippedExisting") ?? 0;

            SummaryText.Text = $"Assigned: {assigned}   |   Skipped (existing): {skipped}   |   Issues: {issues}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   false,
                assignEnabled:  true,
                exportEnabled:  rows.Count > 0);

            ShowIndexWarnings(index);
        }

        // ── Export ───────────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_previewRows == null || _previewRows.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Title      = "Export RBR Object ID Report",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName   = $"RBR_ObjectID_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,ElementId,Category,FamilyName,TypeName," +
                              "Level,LevelCode,RBR_Pr_Code,PBS_R,PBS_S," +
                              "ExistingObjectId,ProposedObjectId,Status,Message");

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var row in _previewRows)
                {
                    sb.AppendLine(
                        $"{Csv(ts)}," +
                        $"{row.ElementIdValue}," +
                        $"{Csv(row.Category)}," +
                        $"{Csv(row.FamilyName)}," +
                        $"{Csv(row.TypeName)}," +
                        $"{Csv(row.LevelName)}," +
                        $"{Csv(row.LevelCode)}," +
                        $"{Csv(row.RbrPrCode)}," +
                        $"{Csv(row.PbsPartR)}," +
                        $"{Csv(row.PbsPartS)}," +
                        $"{Csv(row.ExistingObjectId)}," +
                        $"{Csv(row.ProposedObjectId)}," +
                        $"{Csv(row.Status)}," +
                        $"{Csv(row.Message)}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Report exported to:\n" + dlg.FileName,
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── DataGrid: single-click checkbox support ───────────────────────────

        private void ResultsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                    cell.Focus();
                ResultsGrid.BeginEdit(e);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void ResetPreviewState()
        {
            _previewRows               = null;
            ResultsGrid.ItemsSource    = null;
            ApplyButton.IsEnabled      = false;
            ExportButton.IsEnabled     = false;
            SummaryText.Text           = string.Empty;
            WarningsBorder.Visibility  = Visibility.Collapsed;
        }

        private void SetButtons(bool previewEnabled, bool applyEnabled,
                                 bool assignEnabled, bool exportEnabled)
        {
            PreviewButton.IsEnabled = previewEnabled;
            ApplyButton.IsEnabled   = applyEnabled;
            AssignButton.IsEnabled  = assignEnabled;
            ExportButton.IsEnabled  = exportEnabled;
        }

        private void ShowIndexWarnings(RbrObjectIdIndex index)
        {
            if (index == null) return;
            if (index.Duplicates.Count == 0 && index.Malformed.Count == 0) return;

            var sb = new StringBuilder();
            if (index.Duplicates.Count > 0)
                sb.AppendLine("Duplicate IDs found in model (" + index.Duplicates.Count + "): "
                    + string.Join(", ", index.Duplicates.Take(5))
                    + (index.Duplicates.Count > 5 ? " …" : ""));
            if (index.Malformed.Count > 0)
                sb.AppendLine("Malformed IDs found in model (" + index.Malformed.Count + "): "
                    + string.Join(", ", index.Malformed.Take(5))
                    + (index.Malformed.Count > 5 ? " …" : ""));

            WarningsText.Text         = sb.ToString().Trim();
            WarningsBorder.Visibility = Visibility.Visible;
        }

        private static string MapAssignStatus(string status) => status switch
        {
            "Assigned"        => "Assigned",
            "SkippedExisting" => "Already has Object ID",
            "MissingParameter"=> "Missing RBR-Object_ID parameter",
            "ReadOnly"        => "RBR-Object_ID parameter read-only",
            "MissingLevel"    => "Missing level",
            _                 => status,
        };

        private static string Csv(string v)
        {
            if (v == null) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
