using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CourseManager.Models;
using CourseManager.Services;
using Serilog;

namespace CourseManager.ViewModels;

// ШІ: Claude Code (Claude Opus 5.5) – абстрактний базовий клас редакторів із єдиною обробкою помилок RunSafe
// замість try/catch у кожній команді (за зауваженням викладача).
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
// Кожен блок вікна (курси, роботи, здобувачі, оцінки, дані) – окремий редактор, похідний від цього класу.
public abstract class EditorViewModelBase : ObservableObject
{
    protected EditorViewModelBase(IShellContext shell)
    {
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
    }

    protected IShellContext Shell { get; }
    protected CourseService Service => Shell.Service;

    // Назва блоку вікна – для журналу app.log.
    public abstract string EditorName { get; }

    // Головна ViewModel викликає ці методи для всіх редакторів (поліморфно, через список базового типу).
    public virtual void OnCourseSelected(Course? course) { }
    public virtual void OnStudentSelected(Student? student) { }
    public virtual void OnEnrollmentSelected(Enrollment? enrollment) { }
    public virtual void OnDataChanged() { }

    // Виконує дію; помилку записує в журнал і показує в рядку стану. Повертає true, якщо все вдалося.
    protected bool RunSafe(string action, Action body, string errorPrefix = "Помилка", string errorHint = "")
    {
        try
        {
            body();
            return true;
        }
        catch (Exception ex)
        {
            ReportError(action, ex, errorPrefix, errorHint);
            return false;
        }
    }

    // Те саме для асинхронних команд (фонові операції). Скасування користувачем – не помилка.
    protected async Task<bool> RunSafeAsync(string action, Func<Task> body, string errorPrefix = "Помилка", string errorHint = "")
    {
        try
        {
            await body();
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Information("{Editor}: {Action} скасовано", EditorName, action);
            SetStatus("Операцію скасовано користувачем.");
            OnFailed();
            return false;
        }
        catch (Exception ex)
        {
            ReportError(action, ex, errorPrefix, errorHint);
            return false;
        }
    }

    // Що зробити після помилки (наприклад, скинути прогрес). Перевизначається в похідних класах.
    protected virtual void OnFailed() { }

    protected void SetStatus(string text) => Shell.SetStatus(text);

    protected Task<bool> ConfirmAsync(string message) => Shell.ConfirmAsync(message);

    protected static bool TryParseNumber(string? text, out double value) =>
        double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private void ReportError(string action, Exception ex, string errorPrefix, string errorHint)
    {
        // Помилки введення й правил – попередження, решта (файли, неочікувані) – помилки.
        if (ex is ArgumentException or InvalidOperationException)
            Log.Warning(ex, "{Editor}: помилка – {Action}", EditorName, action);
        else
            Log.Error(ex, "{Editor}: помилка – {Action}", EditorName, action);

        SetStatus($"{errorPrefix}: {CleanMessage(ex)}{errorHint}");
        OnFailed();
    }

    // Повідомлення ArgumentException без технічного хвоста «(Parameter 'value')».
    private static string CleanMessage(Exception ex)
    {
        string msg = ex.Message;
        int tail = msg.IndexOf(" (Parameter", StringComparison.Ordinal);
        return tail > 0 ? msg[..tail] : msg;
    }
}
