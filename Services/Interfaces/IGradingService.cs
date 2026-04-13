using TaskTracker.Models.ViewModels;

namespace TaskTracker.Services.Interfaces;

public interface IGradingService
{
    double CompletionWeight { get; }
    double OnTimeWeight { get; }
    
    double CalculateWeightedScore(int totalTasks, int completedTasks, int onTimeTasks);
    
    (double completionRate, double onTimeRate, double weightedScore) CalculateScores(
        int totalTasks, int completedTasks, int onTimeTasks);
    
    string GetPerformanceLevel(double weightedScore);
    
    List<string> GenerateInsights(EmployeePerformanceViewModel report);
    List<string> GenerateRecommendations(EmployeePerformanceViewModel report);
}