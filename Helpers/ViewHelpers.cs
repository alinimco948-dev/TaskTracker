// Helpers/ViewHelpers.cs
namespace TaskTracker.Helpers;

public static class ViewHelpers
{
    public static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0].Substring(0, 1).ToUpper();
        return (parts[0][0].ToString() + parts[^1][0].ToString()).ToUpper();
    }

    public static (string Text, string BarColor, string BgColor) GetScoreColor(int score)
    {
        return score switch
        {
            >= 90 => ("text-green-600", "#22c55e", "bg-green-100"),
            >= 75 => ("text-blue-600", "#3b82f6", "bg-blue-100"),
            >= 60 => ("text-yellow-600", "#eab308", "bg-yellow-100"),
            _ => ("text-red-600", "#ef4444", "bg-red-100")
        };
    }

    public static string GetStatusBadge(bool isActive)
    {
        if (isActive)
        {
            return "<span class='px-2 py-1 bg-green-100 text-green-700 rounded-full text-xs font-medium'><i class='fas fa-circle text-green-500 mr-1 text-xs'></i>Active</span>";
        }
        return "<span class='px-2 py-1 bg-red-100 text-red-700 rounded-full text-xs font-medium'><i class='fas fa-circle text-red-500 mr-1 text-xs'></i>Inactive</span>";
    }

    public static string FormatDate(DateTime? date, string format = "MMM dd, yyyy")
    {
        return date?.ToString(format) ?? "Not set";
    }

    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }
}