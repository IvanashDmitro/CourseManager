using System.ComponentModel;
using System.Text.Json.Serialization;

namespace CourseManager.Models;

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

    [JsonIgnore]
    public double MaxTotal => Activities.Sum(a => a.MaxScore);

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
