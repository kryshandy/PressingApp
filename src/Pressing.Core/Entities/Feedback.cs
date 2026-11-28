using System.ComponentModel.DataAnnotations;

namespace Pressing.Core.Entities;

/// <summary>Avis d'un client sur une commande retirée (une seule réponse par commande : index unique sur OrderId).</summary>
public class Feedback
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public Order Order { get; set; } = null!;

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    /// <summary>Note de 1 (décevant) à 5 (excellent).</summary>
    public int Note { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
