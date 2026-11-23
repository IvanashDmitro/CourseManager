using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using CourseManager.Models;
using Serilog;

namespace CourseManager.ViewModels;

// Верхня панель: імпорт і експорт JSON, генерація наборів даних, багатопотоковий розрахунок T1/Tn і XML-відомість.
public partial class MainWindowViewModel
{
    // ШІ: Claude Code (Claude Opus 5.5) – доопрацювання імпорту, експорт у файл із поля шляху,
    // генерація великого та стартового наборів (ReplaceWithGeneratedAsync), оновлення таблиці зі збереженням вибору.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
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
            StatusText = $"Файл «{path}» не знайдено. Вкажіть шлях, наприклад backup.json або data\\broken_syntax.json.";
            return;
        }

        if (!await AskAsync($"Завантажити дані з файлу {path}?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено, якщо файл коректний. " +
                            "Якщо файл пошкоджений, поточні дані не зміняться."))
        {
            StatusText = "Імпорт скасовано.";
            return;
        }

        IsBusy = true;
        ProgressValue = 10;
        StatusText = $"Імпорт із {path}...";

        try
        {
            var data = await Task.Run(() => _storage.Load(path));
            _service.ReplaceAll(data);
            var snapshot = _service.CreateSnapshot();
            await Task.Run(() => _storage.Save(DefaultJsonPath, snapshot));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                RefreshCollections();
                StatusText = $"Імпортовано {data.Enrollments.Count} записів із {path}.";
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка імпорту JSON з {Path}", path);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 0;
                StatusText = $"Помилка імпорту: {CleanMessage(ex)} Поточні дані не змінено.";
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (IsBusy) return;

        // Експорт у файл із поля шляху, наприклад backup.json або D:\копія\курси.json.
        string path = JsonFilePath.Trim();
        if (path.Length == 0)
        {
            StatusText = "Вкажіть у полі шлях до файлу для експорту, наприклад backup.json.";
            return;
        }
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            path += ".json";

        IsBusy = true;
        try
        {
            var snapshot = _service.CreateSnapshot();
            await Task.Run(() => _storage.Save(path, snapshot));
            StatusText = $"Експортовано {snapshot.Enrollments.Count} записів у {Path.GetFullPath(path)}.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Помилка експорту JSON");
            StatusText = $"Помилка експорту: {CleanMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ШІ: Claude Code – фонова генерація 10 000+ оцінок для тестування багатопоточності.
    // Змінено: адаптовано під DataGenerator.Generate(15000) та оновлення ProgressBar.
    [RelayCommand]
    private async Task GenerateLargeDatasetAsync()
    {
        if (IsBusy) return;

        if (!await AskAsync("Згенерувати великий набір на 15 000 оцінок (понад 2000 здобувачів, частина з боргами)?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json. " +
                            "Щоб їх зберегти, спочатку зробіть «Експорт JSON», наприклад у backup.json. " +
                            "Повернутися до 100 здобувачів можна кнопкою «Стартовий набір»."))
        {
            StatusText = "Генерацію скасовано.";
            return;
        }

        await ReplaceWithGeneratedAsync("Генерація 15 000 оцінок...",
            () => _generator.Generate(15_000, leaveDebts: true));
    }

    [RelayCommand]
    private async Task ResetStartDataAsync()
    {
        if (IsBusy) return;

        if (!await AskAsync($"Повернути стартовий набір ({StartStudents} здобувачів, частина робіт ще не оцінена)?\n\n" +
                            $"Поточні дані ({_service.Students.Count} здобувачів) буде замінено і перезаписано в courses.json."))
        {
            StatusText = "Повернення стартового набору скасовано.";
            return;
        }

        await ReplaceWithGeneratedAsync("Створення стартового набору...",
            () => _generator.GenerateStartSet(StartStudents));
    }

    private async Task ReplaceWithGeneratedAsync(string busyText, Func<StorageData> generate)
    {
        IsBusy = true;
        ProgressValue = 15;
        StatusText = busyText;

        try
        {
            var data = await Task.Run(generate);
            _service.ReplaceAll(data);
            await Task.Run(() => _storage.Save(DefaultJsonPath, data));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                RefreshCollections();
                int grades = data.Enrollments.Sum(e => e.Grades.Count);
                int debtors = data.Enrollments.Count(e => e.NextActivity != null);
                StatusText = $"Згенеровано {data.Students.Count} здобувачів, {data.Enrollments.Count} записів на курси ({grades:N0} оцінок), " +
                             $"з них {debtors} мають не здані роботи.";
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка генерації великого набору");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ШІ: Claude Code – багатопотоковий розрахунок рейтингу T1/Tn із CancellationToken, IProgress і XML-відомістю.
    // Змінено: додано виведення T1, Tn, прискорення S та збереження course_grade_sheet.xml.
    [RelayCommand]
    private async Task RunBenchmarkAndXmlAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        StatusText = "Виконується розрахунок T1 / Tn...";

        var progress = new Progress<double>(p =>
            Dispatcher.UIThread.InvokeAsync(() => ProgressValue = Math.Round(p * 100, 1)));

        try
        {
            var result = await _service.CalculateRatingsBenchmarkAsync(progress, _cts.Token);
            var enrollments = _service.CreateSnapshot().Enrollments;
            await Task.Run(() => _xmlExporter.Export(DefaultXmlPath, result.Ratings, enrollments), _cts.Token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ProgressValue = 100;
                StatusText = $"Оцінок: {result.GradesProcessed} | T1={result.SingleThreadMs:F2} мс | " +
                             $"Tn({result.ThreadCount})={result.MultiThreadMs:F2} мс | S={result.Speedup:F2}x | XML: {DefaultXmlPath}";
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Розрахунок скасовано користувачем.";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Помилка розрахунку рейтингу");
            StatusText = $"Помилка: {CleanMessage(ex)}";
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
    }
}
