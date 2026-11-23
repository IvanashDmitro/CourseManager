namespace CourseManager.Models;

// ШІ: Claude Code (Claude Opus 5.5) – фабрика робіт курсу для CRUD активностей: створення лабораторної,
// практичної чи тесту за назвою типу і читання/запис додаткового параметра кожного типу.
// Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
public static class ActivityFactory
{
    public const string Lab = "Лабораторна";
    public const string Practice = "Практична";
    public const string Test = "Тест";

    public static IReadOnlyList<string> TypeNames { get; } = [Lab, Practice, Test];

    // Підпис додаткового поля у формі: у кожного типу свій параметр підрахунку балів.
    public static string ExtraLabel(string typeName) => typeName switch
    {
        Lab => "Бонус за захист, балів (0–10)",
        Practice => "Штраф за запізнення, % (0–50)",
        Test => "Кількість питань (1–200)",
        _ => "Додатковий параметр"
    };

    // Типове значення параметра для нової роботи.
    public static double DefaultExtra(string typeName) => typeName switch
    {
        Lab => 2,
        Practice => 15,
        Test => 10,
        _ => 0
    };

    // Створює роботу потрібного типу; некоректні значення відхиляють сеттери самих класів.
    public static CourseActivity Create(string typeName, string title, double maxScore, DateOnly deadline, double extra)
    {
        return typeName switch
        {
            Lab => new LabWork(title, maxScore, deadline, ToWhole(extra, "Бонус за захист")),
            Practice => new Assignment(title, maxScore, deadline, extra / 100.0),
            Test => new Quiz(title, maxScore, deadline, ToWhole(extra, "Кількість питань")),
            _ => throw new ArgumentException($"Невідомий тип роботи: {typeName}.", nameof(typeName))
        };
    }

    public static double GetExtra(CourseActivity activity) => activity switch
    {
        LabWork lab => lab.DefenseBonus,
        Assignment practice => Math.Round(practice.LatePenaltyRate * 100, 2),
        Quiz quiz => quiz.QuestionsCount,
        _ => 0
    };

    // Переносить перевірені значення з тимчасового об'єкта в наявну роботу (тип не змінюється,
    // щоб оцінки здобувачів лишилися прив'язаними до того самого об'єкта).
    public static void CopyValues(CourseActivity from, CourseActivity to)
    {
        if (from.GetType() != to.GetType())
            throw new InvalidOperationException("Тип роботи змінити не можна. Видаліть роботу і створіть нову.");

        to.Title = from.Title;
        to.MaxScore = from.MaxScore;
        to.Deadline = from.Deadline;
        switch (to)
        {
            case LabWork lab: lab.DefenseBonus = ((LabWork)from).DefenseBonus; break;
            case Assignment practice: practice.LatePenaltyRate = ((Assignment)from).LatePenaltyRate; break;
            case Quiz quiz: quiz.QuestionsCount = ((Quiz)from).QuestionsCount; break;
        }
    }

    private static int ToWhole(double value, string what)
    {
        if (Math.Abs(value - Math.Round(value)) > 1e-9)
            throw new ArgumentException($"{what} має бути цілим числом.");
        return (int)Math.Round(value);
    }
}
