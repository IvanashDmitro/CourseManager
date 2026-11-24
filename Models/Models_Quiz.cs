namespace CourseManager.Models;

public sealed class Quiz : CourseActivity
{
    private int _questionsCount = 10;

    public Quiz()
    {
    }

    public Quiz(string title, double maxScore, DateOnly deadline, int questionsCount = 10)
        : base(title, maxScore, deadline)
    {
        QuestionsCount = questionsCount;
    }

    public int QuestionsCount
    {
        get => _questionsCount;
        set
        {
            if (value < 1 || value > 200)
                throw new ArgumentOutOfRangeException(nameof(value), "Кількість питань має бути від 1 до 200.");
            _questionsCount = value;
        }
    }

    public override string TypeName => "Тест";

    public override double CalculateScore(double rawScore, bool isLate)
    {
        ValidateRawScore(rawScore);

        double perQuestion = MaxScore / QuestionsCount;
        int correct = (int)Math.Floor(rawScore / perQuestion + 1e-9);
        double result = correct * perQuestion;

        if (isLate)
            result *= 0.7;
        return Math.Round(result, 2);
    }
}
