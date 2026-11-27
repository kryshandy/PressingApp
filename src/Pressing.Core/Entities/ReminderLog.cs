using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

public class ReminderLog
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int ClientId { get; set; }

    [MaxLength(50)]
    public string ReminderType { get; set; } = string.Empty;

    [MaxLength(30)]
    public string Channel { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.Now;
    public bool Success { get; set; }

    [MaxLength(1000)]
    public string? Error { get; set; }
}
