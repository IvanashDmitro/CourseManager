namespace CourseManager.Models;

public sealed record BenchmarkResult(
    Dictionary<string, double> Ratings,
    double SingleThreadMs,
    double MultiThreadMs,
    int ThreadCount,
    int GradesProcessed)
{
    public double Speedup => MultiThreadMs > 0.001
        ? Math.Round(SingleThreadMs / MultiThreadMs, 2)
        : 1.0;
}
