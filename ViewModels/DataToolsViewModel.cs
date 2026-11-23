using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using CourseManager.Repositories;
using CourseManager.Services;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – редактор верхньої панелі: імпорт і експорт JSON за шляхом із поля,
// генерація великого та стартового наборів, багатопотоковий розрахунок T1/Tn і XML-відомість, скасування.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public partial class DataToolsViewModel : EditorViewModelBase
{
    public const string DefaultJsonPath = "courses.json";
    public const string DefaultXmlPath = "course_grade_sheet.xml";
    public const int StartStudents = 100;
    private const int LargeGradeCount = 15_000;

    private readonly JsonStorage _storage;
    private readonly XmlReportExporter _xmlExporter = new();
    private readonly DataGenerator _generator = new();
    private CancellationTokenSource? _cts;

    public DataToolsViewModel(IShellContext shell, JsonStorage storage) : base(shell)
    {
        _storage = storage;
    }

    public override string EditorName => "Дані та розрахунок";

    // Один шлях і для імпорту, і для експорту JSON.
    [ObservableProperty] private string _jsonFilePath = DefaultJsonPath;
    [ObservableProperty] private bool _isBusy;

    // Після помилки чи скасування смуга прогресу повертається на нуль.
    protected override void OnFailed() => Shell.SetProgress(0);

    // ШІ: Claude Code – фоновий імпорт JSON через Task.Run з оновленням UI через Dispatcher.UIThread.InvokeAsync.
    // Змінено: додано обробку помилок пошкоджених файлів і логування Serilog; підтвердження заміни даних,
    // після успішного імпорту дані одразу зберігаються в courses.json (на екрані й у файлі – те саме).
    [RelayCommand]
    private async Task ImportJsonAsync()
    {
        if (IsBusy) return;

        string path = JsonFilePath.Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            SetStatus($"Файл «{path}» не знайдено. Вкажіть шлях, наприклад backup.json або data\\broken_syntax.json.");
            return;
        }

        if (!await ConfirmAsync($"Завантажити дані з файлу {path}?\n\n" +
                                $"Поточні дані ({Service.Students.Count} здобувачів) буде замінено, якщо файл коректний. " +
                                "Якщо файл пошкоджений, поточні дані не зміняться."))
        {
            SetStatus("Імпорт скасовано.");
            return;
        }

        IsBusy = true;
        Shell.SetProgress(10);
        SetStatus($"Імпорт із {path}...");

        await RunSafeAsync("імпорт JSON", async () =>
        {
            var data = await Task.Run(() => _storage.Load(path));
            Service.ReplaceAll(data);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Shell.NotifyDataChanged();
                Shell.SetProgress(100);
                SetStatus($"Імпортовано {data.Enrollments.Count} записів із {path}.");
            });
        }, "Помилка імпорту", " Поточні дані не змінено.");

        IsBusy = false;
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (IsBusy) return;

        // Експорт у файл із поля шляху, наприклад backup.json або D:\копія\курси.json.
        string path = JsonFilePath.Trim();
        if (path.Length == 0)
        {
            SetStatus("Вкажіть у полі шлях до файлу для експорту, наприклад backup.json.");
            return;
        }
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";

        IsBusy = true;
        await RunSafeAsync("експорт JSON", async () =>
        {
            var snapshot = Service.CreateSnapshot();
            await Task.Run(() => _storage.Save(path, snapshot));
            SetStatus($"Експортовано {snapshot.Enrollments.Count} записів у {Path.GetFullPath(path)}.");
        }, "Помилка експорту");
        IsBusy = false;
    }

    // ШІ: Claude Code – фонова генерація 10 000+ оцінок для тестування багатопоточності.
    // Змінено: адаптовано під DataGenerator.Generate(15000) та оновлення ProgressBar.
    [RelayCommand]
    private async Task GenerateLargeDatasetAsync()
    {
        if (IsBusy) return;

        if (!await ConfirmAsync("Згенерувати великий набір на 15 000 оцінок (понад 2000 здобувачів, частина з боргами)?\n\n" +
                                $"Поточні дані ({Service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json. " +
                                "Щоб їх зберегти, спочатку зробіть «Експорт JSON», наприклад у backup.json. " +
                                "Повернутися до 100 здобувачів можна кнопкою «Стартовий набір»."))
        {
            SetStatus("Генерацію скасовано.");
            return;
        }

        await ReplaceWithGeneratedAsync("Генерація 15 000 оцінок...",
            () => _generator.Generate(LargeGradeCount, leaveDebts: true));
    }

    [RelayCommand]
    private async Task ResetStartDataAsync()
    {
        if (IsBusy) return;

        if (!await ConfirmAsync($"Повернути стартовий набір ({StartStudents} здобувачів, частина робіт ще не оцінена)?\n\n" +
                                $"Поточні дані ({Service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json."))
        {
            SetStatus("Повернення стартового набору скасовано.");
            return;
        }

        await ReplaceWithGeneratedAsync("Створення стартового набору...",
            () => _generator.GenerateStartSet(StartStudents));
    }

    private async Task ReplaceWithGeneratedAsync(string busyText, Func<StorageData> generate)
    {
        IsBusy = true;
        Shell.SetProgress(15);
        SetStatus(busyText);

        await RunSafeAsync("генерація даних", async () =>
        {
            var data = await Task.Run(generate);
            Service.ReplaceAll(data);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Shell.NotifyDataChanged();
                Shell.SetProgress(100);
                int grades = data.Enrollments.Sum(e => e.Grades.Count);
                int debtors = data.Enrollments.Count(e => e.NextActivity != null);
                SetStatus($"Згенеровано {data.Students.Count} здобувачів, {data.Enrollments.Count} записів на курси ({grades:N0} оцінок), " +
                          $"з них {debtors} мають не здані роботи.");
            });
        });

        IsBusy = false;
    }

    // ШІ: Claude Code – багатопотоковий розрахунок рейтингу T1/Tn із CancellationToken, IProgress і XML-відомістю.
    // Змінено: додано виведення T1, Tn, прискорення S та збереження course_grade_sheet.xml.
    [RelayCommand]
    private async Task RunBenchmarkAndXmlAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Shell.SetProgress(0);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetStatus("Виконується розрахунок T1 / Tn...");

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.InvokeAsync(() => Shell.SetProgress(Math.Round(p * 100, 1))));

        await RunSafeAsync("розрахунок T1/Tn", async () =>
        {
            var result = await Service.CalculateRatingsBenchmarkAsync(progress, token);
            var enrollments = Service.CreateSnapshot().Enrollments;
            await Task.Run(() => _xmlExporter.Export(DefaultXmlPath, result.Ratings, enrollments), token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Shell.SetProgress(100);
                SetStatus($"Оцінок: {result.GradesProcessed} | T1={result.SingleThreadMs:F2} мс | " +
                          $"Tn({result.ThreadCount})={result.MultiThreadMs:F2} мс | S={result.Speedup:F2}x | XML: {DefaultXmlPath}");
            });
        });

        _cts.Dispose();
        _cts = null;
        IsBusy = false;
    }

    [RelayCommand]
    private void CancelOperation() => _cts?.Cancel();
}
