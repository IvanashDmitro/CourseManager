using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CourseManager.Models;

// ШІ: Claude Code (Claude Opus 5.5) – INotifyPropertyChanged і DisplayName (оновлення назви в інтерфейсі),
// AddStandardActivities() (стартовий набір робіт на 100 балів), MaxTotal (максимум курсу для рейтингу).
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
// INotifyPropertyChanged: після «Оновити» нова назва курсу одразу видна в таблиці та списку.
public sealed class Course : INotifyPropertyChanged
{
    private string _code = string.Empty;
    private string _title = string.Empty;
    private string _instructor = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged(string name)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
    }

    // DisplayName використовується у випадному списку курсів.
    [JsonIgnore]
    public string DisplayName => $"{Code} – {Title}";

    public Course()
    {
    }

    public Course(string code, string title, string instructor)
    {
        Code = code;
        Title = title;
        Instructor = instructor;
    }

    public string Code
    {
        get => _code;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Код курсу не може бути порожнім.", nameof(value));
            _code = value.Trim().ToUpperInvariant();
            OnChanged(nameof(Code));
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Назва курсу не може бути порожньою.", nameof(value));
            _title = value.Trim();
            OnChanged(nameof(Title));
        }
    }

    public string Instructor
    {
        get => _instructor;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Ім'я викладача не може бути порожнім.", nameof(value));
            _instructor = value.Trim();
            OnChanged(nameof(Instructor));
        }
    }

    public List<CourseActivity> Activities { get; set; } = new();

    // Сума максимальних балів усіх робіт курсу. Рейтинг рахується у відсотках від неї (шкала 0–100),
    // тож після додавання чи зміни робіт оцінки за національною шкалою лишаються коректними.
    [JsonIgnore]
    public double MaxTotal => Activities.Sum(a => a.MaxScore);

    // Стандартний набір робіт нового курсу на 100 балів (далі його можна змінювати в блоці «Роботи курсу»):
    // 4 лабораторні по 15 (60) + 2 практичні по 10 (20) + 2 тести по 10 (20).
    public void AddStandardActivities(DateOnly start)
    {
        for (int n = 1; n <= 4; n++)
            Activities.Add(new LabWork($"Лабораторна {n}", 15, start.AddDays(7 * n), 2));

        for (int n = 1; n <= 2; n++)
        {
            Activities.Add(new Assignment($"Практична {n}", 10, start.AddDays(14 * n + 3), 0.15));
            Activities.Add(new Quiz($"Тест {n}", 10, start.AddDays(14 * n + 5), 10));
        }
    }

    public override string ToString() => DisplayName;
}
