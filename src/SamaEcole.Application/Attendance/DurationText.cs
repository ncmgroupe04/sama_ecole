namespace SamaEcole.Application.Attendance;

/// <summary>Durée en clair pour les billets : « 45 min », « 2 h », « 2 h 20 ». Pure, partagée par le PDF et les écrans serveur.</summary>
public static class DurationText
{
    public static string Human(int minutes)
    {
        if (minutes < 60) return $"{minutes} min";

        var hours = minutes / 60;
        var rest = minutes % 60;
        return rest == 0 ? $"{hours} h" : $"{hours} h {rest:00}";
    }
}
