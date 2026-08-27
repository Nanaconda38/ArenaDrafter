using System.Windows;

namespace ArenaDrafter;

public partial class BugReportWindow : Window
{
    public BugReportWindow(bool previousCrashDetected = false)
    {
        InitializeComponent();
        AreaBox.ItemsSource = new[] { "Connection", "Draft", "Ban", "Leader", "Battle Opener", "Rewards / refill", "Session", "Crash", "User interface", "Other" };
        AreaBox.SelectedItem = previousCrashDetected ? "Crash" : "Other";
        if (previousCrashDetected)
        {
            SummaryBox.Text = "ArenaDrafter closed unexpectedly";
            ActualBox.Text = "ArenaDrafter did not close normally during the previous session.";
        }
    }

    public string Area => AreaBox.SelectedItem as string ?? "Other";
    public string Summary => SummaryBox.Text.Trim();
    public string Expected => ExpectedBox.Text.Trim();
    public string Actual => ActualBox.Text.Trim();
    public string Steps => StepsBox.Text.Trim();
    public bool IncludeConfiguration => IncludeConfigurationBox.IsChecked == true;

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Summary))
        {
            MessageBox.Show(this, "Enter a short summary before creating the report.", "ArenaDrafter", MessageBoxButton.OK, MessageBoxImage.Information);
            SummaryBox.Focus();
            return;
        }
        DialogResult = true;
    }
}
