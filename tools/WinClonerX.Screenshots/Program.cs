using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinClonerX.App;
using WinClonerX.App.Converters;
using WinClonerX.App.Views;
using WinClonerX.Domain.Execution;

/// <summary>Runs the localized screenshot capture sequence.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var output = GetOutputDirectory(args);
        if (output is null)
        {
            return 2;
        }

        Directory.CreateDirectory(Path.Combine(output, "en"));
        Directory.CreateDirectory(Path.Combine(output, "es"));

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        // Same Fluent theme as App.xaml, so the controls look as they do in the real app. The API is still marked for evaluation.
#pragma warning disable WPF0001
        app.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
        var appResources = (ResourceDictionary)Application.LoadComponent(
            new Uri("/WinClonerX.App;component/Themes/WinClonerXTheme.xaml", UriKind.Relative));
        app.Resources.MergedDictionaries.Add(appResources);
        app.Resources["LocKeyConverter"] = new LocKeyConverter();
        app.Resources["BoolToVisibility"] = new BoolToVisibilityConverter();
        app.Resources["NullToVisibility"] = new NullToVisibilityConverter();

        app.Startup += async (_, _) =>
        {
            foreach (var (cultureName, folder) in new[] { ("en-US", "en"), ("es-ES", "es") })
            {
                await CaptureLanguageAsync(app, output, cultureName, folder);
            }

            app.Shutdown();
        };

        app.Run();
        return 0;
    }

    private static string? GetOutputDirectory(string[] args)
    {
        if (args.Length == 0)
        {
            return Path.GetFullPath("docs/screenshots");
        }

        if (args.Length == 2 && args[0] == "--out")
        {
            return Path.GetFullPath(args[1]);
        }

        Console.Error.WriteLine("Usage: WinClonerX.Screenshots [--out <directory>]");
        return null;
    }

    private static async Task CaptureLanguageAsync(Application app, string output, string cultureName, string folder)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var scenario = new Scenario();
        using var window = new WindowLifetime(scenario.Window);
        scenario.Load();
        ScreenshotDispatcher.Flush();

        if (scenario.Window.Content is Grid root)
        {
            root.Background = scenario.Window.Background;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>((DependencyObject)scenario.Window.Content);
        scrollViewer?.ScrollToVerticalOffset(0);
        scrollViewer?.UpdateLayout();
        ScreenshotDispatcher.Flush();
        Capture(scenario.Window, Path.Combine(output, folder, "01-disks.png"));

        await scenario.Wizard.NextCommand.ExecuteAsync(null);
        scenario.Review.VerifyAllData = true;
        ScreenshotDispatcher.Flush();
        Capture(scenario.Window, Path.Combine(output, folder, "02-review.png"));

        await scenario.Wizard.NextCommand.ExecuteAsync(null);
        ScreenshotDispatcher.Flush();
        scenario.Confirm.TypedSuffix = "4815";
        ScreenshotDispatcher.Flush();
        Capture(scenario.Window, Path.Combine(output, folder, "03-confirm.png"));

        if (!scenario.Confirm.CloneNowCommand.CanExecute(null))
        {
            throw new InvalidOperationException(
                $"The confirmation is not ready: valid={scenario.Confirm.IsSuffixValid}, ready={scenario.Confirm.IsReadyToClone}.");
        }

        scenario.Confirm.CloneNowCommand.Execute(null);
        scenario.Execute.WaitUntilStarted();
        scenario.Execute.Publish(new ExecutionStarted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow));
        PublishProgressAndWarmup(scenario);
        Capture(scenario.Window, Path.Combine(output, folder, "04-progress.png"));

        PublishCompletion(scenario);
        scenario.Execute.Complete(ScreenshotScenario.CreateResultReport(scenario.ConfirmedPlan));
        await scenario.Wizard.ExecutionCompletion;
        ScreenshotDispatcher.Flush();
        Capture(scenario.Window, Path.Combine(output, folder, "05-result.png"));

        scenario.Review.ShowVerifyAllInfoCommand.Execute(null);
        var dialog = new InfoDialog(scenario.Info.LastRequest!)
        {
            Owner = scenario.Window,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            Width = 480,
        };
        dialog.Show();
        ScreenshotDispatcher.Flush();
        dialog.UpdateLayout();
        var dialogBitmap = ScreenshotRenderer.Render(dialog, dialog.ActualWidth, dialog.ActualHeight);
        ScreenshotRenderer.CompositeDialog(
            Path.Combine(output, folder, "02-review.png"),
            dialogBitmap,
            Path.Combine(output, folder, "06-verify-info.png"));
        dialog.Close();
        scenario.Window.Close();
    }

    private static void PublishProgressAndWarmup(Scenario scenario)
    {
        foreach (var (phase, duration) in ScreenshotScenario.WarmupDurations)
        {
            scenario.Execute.Publish(new PhaseStarted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow, phase));
            scenario.Execute.Publish(new PhaseCompleted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow, phase, duration));
        }

        var plan = scenario.ConfirmedPlan;
        var totalBytes = plan.Estimate.BytesToCopy.Bytes;
        var bytesDone = totalBytes / 2;
        var bytesRemaining = totalBytes - bytesDone;
        var eta = ScreenshotScenario.DurationAtThroughput(bytesRemaining, ScreenshotScenario.ThroughputBytesPerSecond);
        scenario.Execute.Publish(new PhaseStarted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow, ExecutionPhase.CopyData));
        scenario.Execute.Publish(new ProgressReported(
            ScreenshotScenario.RunId,
            ScreenshotScenario.FixedNow,
            new CloneProgress(bytesDone, totalBytes, ScreenshotScenario.ThroughputBytesPerSecond, eta)));
        ScreenshotDispatcher.Flush();
    }

    private static void PublishCompletion(Scenario scenario)
    {
        scenario.Execute.Publish(new PhaseCompleted(
            ScreenshotScenario.RunId,
            ScreenshotScenario.FixedNow,
            ExecutionPhase.CopyData,
            ScreenshotScenario.CopyDuration(scenario.ConfirmedPlan)));

        foreach (var (phase, duration) in ScreenshotScenario.CompletionDurations)
        {
            scenario.Execute.Publish(new PhaseStarted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow, phase));
            scenario.Execute.Publish(new PhaseCompleted(ScreenshotScenario.RunId, ScreenshotScenario.FixedNow, phase, duration));
        }
    }

    private static void Capture(MainWindow window, string path)
    {
        var content = (FrameworkElement)window.Content;
        window.UpdateLayout();
        ScreenshotDispatcher.Flush();
        content.Measure(new Size(1000, 680));
        content.Arrange(new Rect(0, 0, 1000, 680));
        ScreenshotRenderer.Save(ScreenshotRenderer.Render(content, 1000, 680), path);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Keeps a capture window alive until its language screenshots are saved.</summary>
    private sealed class WindowLifetime(MainWindow window) : IDisposable
    {
        public void Dispose()
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }
}
