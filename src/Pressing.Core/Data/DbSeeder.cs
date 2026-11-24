using Microsoft.EntityFrameworkCore;
using Pressing.Core.Entities;
using Pressing.Core.Security;

namespace Pressing.Core.Data;

public static class DbSeeder
{
    private static readonly (string Name, string Icon, decimal Price)[] Categories =
    [
        ("Chemise", "\uE7C1", 1000m),
        ("Pantalon", "\uE8B7", 1500m),
        ("Costume 2 pièces", "\uE8F1", 3000m),
        ("Costume 3 pièces", "\uE8F1", 4000m),
        ("Robe simple", "\uE7C0", 2000m),
        ("Robe de soirée", "\uE7C0", 3500m),
        ("Jupe", "\uE7C2", 1500m),
        ("Chemisier", "\uE7C1", 1500m),
        ("Pull", "\uE7C1", 1500m),
        ("Manteau", "\uE7B8", 3000m),
        ("Doudoune", "\uE7B8", 3500m),
        ("Jean", "\uE8B7", 1500m),
        ("Blouson", "\uE7B8", 2500m),
        ("Cravate", "\uE8D4", 800m),
        ("Écharpe", "\uE8D4", 800m),
        ("Couverture", "\uE7C3", 2500m),
        ("Drap 2 places", "\uE7C3", 2000m),
        ("Rideau", "\uE7C3", 2500m),
        ("Nappe", "\uE7C3", 1500m),
        ("Tapis", "\uE7C3", 3000m)
    ];

    public static async Task SeedSettingsAsync(PressingDbContext db, CancellationToken ct = default)
    {
        var existing = await db.AppSettings.Select(s => s.Key).ToListAsync(ct);
        foreach ((string key, string value) in SettingsKeys.Defaults)
        {
            if (!existing.Contains(key))
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedStaffAsync(
        PressingDbContext db,
        string adminUsername,
        string adminPassword,
        CancellationToken ct = default)
    {
        if (await db.StaffUsers.AnyAsync(ct))
        {
            return;
        }

        db.StaffUsers.AddRange(
            new StaffUser
            {
                FullName = "Administrateur",
                Username = adminUsername,
                Email = "admin@pressing-eclat.local",
                PasswordHash = PasswordHasher.Hash(adminPassword),
                Role = UserRole.Admin
            },
            new StaffUser
            {
                FullName = "Awa Manager",
                Username = "manager",
                Email = "manager@pressing-eclat.local",
                PasswordHash = PasswordHasher.Hash("Manager@2026!"),
                Role = UserRole.Manager
            },
            new StaffUser
            {
                FullName = "Koffi Caisse",
                Username = "caisse",
                Email = "caisse@pressing-eclat.local",
                PasswordHash = PasswordHasher.Hash("Caisse@2026!"),
                Role = UserRole.Cashier
            });

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedCatalogAsync(PressingDbContext db, CancellationToken ct = default)
    {
        if (await db.Categories.AnyAsync(ct))
        {
            return;
        }

        int sort = 0;
        foreach ((string name, string icon, decimal basePrice) in Categories)
        {
            var category = new Category
            {
                Name = name,
                Icon = icon,
                SortOrder = sort += 10
            };

            foreach (ServiceType service in Enum.GetValues<ServiceType>())
            {
                decimal multiplier = service switch
                {
                    ServiceType.Washing => 0.75m,
                    ServiceType.WashAndIron => 1m,
                    ServiceType.Ironing => 0.55m,
                    ServiceType.DryCleaning => 1.6m,
                    ServiceType.StainRemoval => 0.9m,
                    _ => 1m
                };

                decimal price = RoundToHundred(basePrice * multiplier);
                decimal express = Math.Max(300m, RoundToHundred(price * 0.4m));
                int days = service == ServiceType.DryCleaning ? 5 : 3;

                category.Prices.Add(new CategoryPrice
                {
                    ServiceType = service,
                    Price = price,
                    ExpressSupplement = express,
                    EstimatedDays = days
                });
            }

            db.Categories.Add(category);
        }

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedPromotionsAsync(PressingDbContext db, CancellationToken ct = default)
    {
        if (await db.Promotions.AnyAsync(ct))
        {
            return;
        }

        DateTime now = DateTime.Now;
        db.Promotions.AddRange(
            new Promotion
            {
                Code = "BIENVENUE10",
                Description = "10 % de remise pour la première commande",
                Type = PromotionType.Percentage,
                Value = 10m,
                MinOrderAmount = 5000m,
                ValidFrom = now.AddMonths(-1),
                ValidTo = now.AddYears(1),
                MaxUses = 0,
                IsActive = true
            },
            new Promotion
            {
                Code = "EXPRESS500",
                Description = "500 F de remise sur une commande express",
                Type = PromotionType.FixedAmount,
                Value = 500m,
                MinOrderAmount = 3000m,
                ValidFrom = now.AddMonths(-1),
                ValidTo = now.AddYears(1),
                MaxUses = 0,
                IsActive = true
            });

        await db.SaveChangesAsync(ct);
    }

    public static async Task SeedDemoAsync(PressingDbContext db, CancellationToken ct = default)
    {
        if (await db.Clients.AnyAsync(ct))
        {
            return;
        }

        var clients = new[]
        {
            NewClient("Awa", "Koné", "+225 07 11 22 33 44", "awa.kone@example.com", "Cocody, Riviera 3", true),
            NewClient("Koffi", "Yao", "+225 05 22 33 44 55", "koffi.yao@example.com", "Yopougon, Niangon", true),
            NewClient("Mariam", "Traoré", "+225 01 33 44 55 66", "mariam.traore@example.com", "Marcory, Zone 4", true),
            NewClient("Jean-Marc", "Bamba", "+225 07 44 55 66 77", "jm.bamba@example.com", "Plateau, rue du Commerce", false),
            NewClient("Fatou", "Diabaté", "+225 05 55 66 77 88", "fatou.diabate@example.com", "Treichville, avenue 16", true),
            NewClient("Serge", "Gnahoré", "+225 01 66 77 88 99", "serge.gnahore@example.com", "Adjamé, Liberty", false),
            NewClient("Aya", "Kouassi", "+225 07 77 88 99 00", "aya.kouassi@example.com", "Cocody, Angré", true),
            NewClient("Ibrahim", "Coulibaly", "+225 05 88 99 00 11", "ibrahim.coulibaly@example.com", "Abobo, Baoulé", false)
        };

        db.Clients.AddRange(clients);
        await db.SaveChangesAsync(ct);

        var categories = await db.Categories
            .Include(c => c.Prices)
            .ToDictionaryAsync(c => c.Name, c => c, ct);

        var admin = await db.StaffUsers.FirstAsync(s => s.Role == UserRole.Admin, ct);
        DateTime now = DateTime.Now;

        var order1 = BuildOrder(clients[0], categories["Chemise"], categories["Pantalon"], now.AddDays(-1), OrderStatus.Ready, true, admin, 0.5m, PaymentMethod.Cash);
        var order2 = BuildOrder(clients[1], categories["Costume 2 pièces"], categories["Chemise"], now.AddHours(-4), OrderStatus.InProgress, false, admin, 0.4m, PaymentMethod.MobileMoney);
        var order3 = BuildOrder(clients[2], categories["Robe de soirée"], categories["Jupe"], now.AddDays(-3), OrderStatus.Ready, false, admin, 1m, PaymentMethod.Card);
        var order4 = BuildOrder(clients[3], categories["Manteau"], categories["Pull"], now.AddDays(-5), OrderStatus.Completed, false, admin, 1m, PaymentMethod.Cash);
        var order5 = BuildOrder(clients[4], categories["Couverture"], categories["Drap 2 places"], now.AddHours(-2), OrderStatus.Deposited, false, admin, 0.3m, PaymentMethod.Cash);
        var order6 = BuildOrder(clients[5], categories["Costume 3 pièces"], categories["Cravate"], now.AddDays(-2), OrderStatus.Ready, false, admin, 0m, PaymentMethod.Cash);
        var order7 = BuildOrder(clients[6], categories["Rideau"], categories["Nappe"], now.AddDays(-7), OrderStatus.Completed, false, admin, 1m, PaymentMethod.MobileMoney);
        var order8 = BuildOrder(clients[7], categories["Blouson"], categories["Jean"], now.AddHours(-1), OrderStatus.Deposited, true, admin, 0.5m, PaymentMethod.Cash);

        db.Orders.AddRange(order1, order2, order3, order4, order5, order6, order7, order8);
        await db.SaveChangesAsync(ct);

        foreach (Order order in new[] { order1, order2, order3, order4, order5, order6, order7, order8 })
        {
            order.TicketNumber = $"PRESS-{order.CreatedAt:yyyy}-{order.Id:00000}";
        }

        var pickup4 = new Pickup
        {
            OrderId = order4.Id,
            ClientId = order4.ClientId,
            PickedUpAt = order4.CreatedAt.AddDays(3),
            StaffUserId = admin.Id,
            Note = "Retrait complet",
            Items = order4.Items.Select(i => new PickupItem
            {
                OrderItemId = i.Id,
                Quantity = i.Quantity
            }).ToList()
        };
        var pickup7 = new Pickup
        {
            OrderId = order7.Id,
            ClientId = order7.ClientId,
            PickedUpAt = order7.CreatedAt.AddDays(4),
            StaffUserId = admin.Id,
            Note = "Retrait complet",
            Items = order7.Items.Select(i => new PickupItem
            {
                OrderItemId = i.Id,
                Quantity = i.Quantity
            }).ToList()
        };
        db.Pickups.AddRange(pickup4, pickup7);

        await db.SaveChangesAsync(ct);

        foreach (Order order in new[] { order1, order2, order3, order4, order5, order6, order7, order8 })
        {
            if (order.PaidAmount > 0)
            {
                db.Payments.Add(new Payment
                {
                    OrderId = order.Id,
                    ClientId = order.ClientId,
                    Amount = order.PaidAmount,
                    Type = PaymentType.Payment,
                    Method = order.Id % 2 == 0 ? PaymentMethod.MobileMoney : PaymentMethod.Cash,
                    PaidAt = order.CreatedAt.AddMinutes(15),
                    Reference = $"DEMO-{order.Id:0000}",
                    StaffUserId = admin.Id,
                    Note = "Encaissement de démonstration"
                });

                int points = (int)Math.Floor(order.PaidAmount / 1000m);
                if (points > 0)
                {
                    order.Client.LoyaltyPoints += points;
                    db.LoyaltyTransactions.Add(new LoyaltyTransaction
                    {
                        ClientId = order.ClientId,
                        OrderId = order.Id,
                        Points = points,
                        Type = LoyaltyTransactionType.Earned,
                        Note = $"Points gagnés — {order.TicketNumber}",
                        CreatedAt = order.CreatedAt.AddMinutes(16)
                    });
                }
            }

            if (order.Status is OrderStatus.Ready or OrderStatus.Completed or OrderStatus.PartiallyPickedUp)
            {
                db.EmailMessages.Add(new EmailMessage
                {
                    ToEmail = order.Client.Email ?? "client@example.com",
                    ToName = order.Client.FullName,
                    Subject = $"Vos articles sont prêts — {order.TicketNumber}",
                    HtmlBody = $"<p>Bonjour {order.Client.FirstName},</p><p>Votre commande <strong>{order.TicketNumber}</strong> est prête.</p>",
                    TextBody = $"Bonjour {order.Client.FirstName}, votre commande {order.TicketNumber} est prête.",
                    Template = "order-ready",
                    Status = EmailStatus.Simulated,
                    CreatedAt = order.CreatedAt.AddDays(1),
                    SentAt = order.CreatedAt.AddDays(1).AddMinutes(1),
                    RelatedOrderId = order.Id,
                    RelatedClientId = order.ClientId
                });
            }

            db.StaffNotifications.Add(new StaffNotification
            {
                Title = order.Status == OrderStatus.Ready ? "Commande prête" : "Nouvelle commande",
                Message = $"{order.TicketNumber} — {order.Client.FullName}",
                Kind = order.Status == OrderStatus.Ready ? NotificationKind.Success : NotificationKind.Info,
                Link = $"/orders/{order.Id}",
                CreatedAt = order.CreatedAt,
                IsRead = order.CreatedAt < now.AddDays(-2)
            });

            db.AuditLogs.Add(new AuditLog
            {
                Actor = "Système",
                Action = "seed",
                EntityType = nameof(Order),
                EntityId = order.Id.ToString(),
                Details = $"Commande de démonstration {order.TicketNumber}",
                CreatedAt = order.CreatedAt
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static Client NewClient(
        string firstName,
        string lastName,
        string phone,
        string email,
        string address,
        bool webAccess)
    {
        return new Client
        {
            FirstName = firstName,
            LastName = lastName,
            Phone = phone,
            PhoneNormalized = PhoneNormalizer.Normalize(phone),
            WhatsApp = phone,
            Email = email,
            Address = address,
            WebAccessEnabled = webAccess,
            PasswordHash = webAccess ? PasswordHasher.Hash("Client@2026!") : null,
            CreatedAt = DateTime.Now.AddDays(-Random.Shared.Next(20, 90))
        };
    }

    private static Order BuildOrder(
        Client client,
        Category firstCategory,
        Category secondCategory,
        DateTime createdAt,
        OrderStatus status,
        bool express,
        StaffUser staff,
        decimal paidRatio,
        PaymentMethod method)
    {
        var firstPrice = firstCategory.Prices.First(p => p.ServiceType == ServiceType.WashAndIron);
        var secondPrice = secondCategory.Prices.First(p => p.ServiceType == ServiceType.WashAndIron);

        var order = new Order
        {
            ClientId = client.Id,
            Client = client,
            CreatedAt = createdAt,
            DueDate = createdAt.AddDays(express ? 1 : 3),
            IsExpress = express,
            CreatedByStaffId = staff.Id,
            Status = status,
            Notes = status == OrderStatus.Ready ? "Articles prêts, client à prévenir." : null
        };

        var items = new List<OrderItem>
        {
            new()
            {
                CategoryId = firstCategory.Id,
                ServiceType = ServiceType.WashAndIron,
                Quantity = 1,
                UnitPrice = firstPrice.Price,
                ExpressSupplement = firstPrice.ExpressSupplement,
                IsExpress = express,
                Status = status switch
                {
                    OrderStatus.Deposited => ItemStatus.Deposited,
                    OrderStatus.InProgress => ItemStatus.InProgress,
                    OrderStatus.Ready => ItemStatus.Ready,
                    OrderStatus.Completed => ItemStatus.PickedUp,
                    OrderStatus.PartiallyPickedUp => ItemStatus.PickedUp,
                    _ => ItemStatus.Deposited
                },
                DueDate = createdAt.AddDays(express ? 1 : 3),
                PickedUpQuantity = status is OrderStatus.Completed or OrderStatus.PartiallyPickedUp ? 1 : 0,
                PickedUpAt = status is OrderStatus.Completed or OrderStatus.PartiallyPickedUp ? createdAt.AddDays(2) : null
            },
            new()
            {
                CategoryId = secondCategory.Id,
                ServiceType = ServiceType.WashAndIron,
                Quantity = 2,
                UnitPrice = secondPrice.Price,
                ExpressSupplement = secondPrice.ExpressSupplement,
                IsExpress = express,
                Status = status switch
                {
                    OrderStatus.Deposited => ItemStatus.Deposited,
                    OrderStatus.InProgress => ItemStatus.Deposited,
                    OrderStatus.Ready => ItemStatus.Ready,
                    OrderStatus.Completed => ItemStatus.PickedUp,
                    OrderStatus.PartiallyPickedUp => ItemStatus.Ready,
                    _ => ItemStatus.Deposited
                },
                DueDate = createdAt.AddDays(express ? 1 : 3),
                PickedUpQuantity = status == OrderStatus.Completed ? 2 : 0,
                PickedUpAt = status == OrderStatus.Completed ? createdAt.AddDays(2) : null
            }
        };

        foreach (OrderItem item in items)
        {
            order.Subtotal += item.UnitPrice * item.Quantity;
            order.ExpressTotal += (item.IsExpress ? item.ExpressSupplement : 0m) * item.Quantity;
        }

        order.Total = order.Subtotal + order.ExpressTotal;
        order.PaidAmount = Math.Round(order.Total * paidRatio, 0, MidpointRounding.AwayFromZero);
        order.CompletedAt = status == OrderStatus.Completed ? createdAt.AddDays(2) : null;
        order.ReadyNotifiedAt = status is OrderStatus.Ready or OrderStatus.Completed ? createdAt.AddDays(1) : null;
        order.Items = items;
        return order;
    }

    private static decimal RoundToHundred(decimal value) =>
        Math.Round(value / 100m, MidpointRounding.AwayFromZero) * 100m;
}
