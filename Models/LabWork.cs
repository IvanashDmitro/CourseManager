namespace CourseManager.Models;

public sealed class LabWork : CourseActivity
{
    private int _defenseBonus;

    public LabWork()
    {
    }

    public LabWork(string title, double maxScore, DateOnly deadline, int defenseBonus = 0)
        : base(title, maxScore, deadline)
    {
        DefenseBonus = defenseBonus;
    }

    public int DefenseBonus
    {
        get => _defenseBonus;
        set
        {
            if (value < 0 || value > 10)
                throw new ArgumentOutOfRangeException(nameof(value), "Бонус за захист має бути від 0 до 10.");
            _defenseBonus = value;
        }
    }

    public override string TypeName => "Лабораторна";

    // ШІ: Claude Code (Claude Opus 5.5) – виправлення: бонус за захист не додається до нульового балу.
    // Змінено: [ЗАПОВНИТИ – що змінив або перевірив студент]
    public override double CalculateScore(double rawScore, bool isLate)
    {
        ValidateRawScore(rawScore);
        // Бонус за захист лише тоді, коли робота здана вчасно і має хоч якийсь бал:
        // за нульову роботу «захищати» нічого.
        double adjusted = isLate ? rawScore * 0.85
            : rawScore > 0 ? rawScore + DefenseBonus
            : 0;
        return Math.Round(Math.Min(adjusted, MaxScore), 2);
    }
}
