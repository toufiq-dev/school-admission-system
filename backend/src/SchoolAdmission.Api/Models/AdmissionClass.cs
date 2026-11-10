namespace SchoolAdmission.Api.Models;

public class AdmissionClass
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalSeats { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Hold> Holds { get; set; } = new();
}
