using System.Reflection;
using System.Text;
using Pressing.Client;
using Pressing.Core;
using Pressing.Core.Dtos;
using Pressing.Desktop.Dialogs;
using Pressing.Desktop.Forms;
using Pressing.Desktop.Services;
using Pressing.Desktop.Ui;
using Pressing.Desktop.Views;

namespace Pressing.Desktop;

/// <summary>
/// Mode <c>--selftest</c> : exécute, sans interaction, un scénario complet contre l'API (connexion, toutes les vues,
/// dépôt, paiement, statuts, retrait, reçu PDF…) et écrit « SELFTEST OK » ou l'exception dans
/// <c>%TEMP%\pressing-selftest.log</c>. Code de sortie 0 (succès) ou 1 (échec).
/// <c>--screens &lt;dossier&gt;</c> enregistre en plus une capture PNG de chaque écran.
/// </summary>
internal static class SelfTest
{
    private static readonly StringBuilder Log = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "pressing-selftest.log");

    public static int Run(string apiUrl, string? screensDir)
    {
        File.WriteAllText(LogPath, string.Empty);
        AppDialogs.Headless = true;
        int exitCode = 1;
        Form host = new()
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-5000, -5000),
            ClientSize = TestSize(),
            BackColor = Theme.Background
        };
        host.Shown += async (_, _) =>
        {
            try
            {
                await Scenario(apiUrl, screensDir, host);
                Write("SELFTEST OK");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Write("SELFTEST ÉCHEC : " + ex);
                exitCode = 1;
            }
            finally
            {
                host.Close();
            }
        };
        Application.Run(host);
        return exitCode;
    }

    private static Size TestSize()
    {
        string? value = Environment.GetEnvironmentVariable("PRESSING_TEST_SIZE");
        string[]? parts = value?.Split('x');
        return parts is { Length: 2 } && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)
            ? new Size(w, h)
            : new Size(1440, 900);
    }

    private static IEnumerable<Size> ShellSizes()
    {
        string value = Environment.GetEnvironmentVariable("PRESSING_TEST_SIZES") ?? "1280x760,1600x900";
        foreach (string part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] wh = part.Split('x');
            if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h))
            {
                yield return new Size(w, h);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);

    /// <summary>Redimensionne sans le plafond « taille de l'écran » de Windows Forms (captures 1600×900 sur un écran plus petit).</summary>
    private static void ResizeWindow(Form form, Size size)
    {
        SetWindowPos(form.Handle, IntPtr.Zero, form.Left, form.Top, size.Width, size.Height, 0x0004 | 0x0010);
        form.PerformLayout();
    }

    /// <summary>Capture réelle d'une fenêtre (barre de titre DWM sombre, textes d'aide) ; repli sur DrawToBitmap.</summary>
    private static Bitmap? PrintForm(Form form)
    {
        try
        {
            Bitmap bitmap = new(form.Width, form.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                try
                {
                    ok = PrintWindow(form.Handle, hdc, 2);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }

                if (!ok)
                {
                    bitmap.Dispose();
                    return null;
                }
            }

            // Une capture DWM d'une fenêtre hors écran peut être noire : on vérifie quelques pixels.
            Color probe = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
            Color corner = bitmap.GetPixel(Math.Min(bitmap.Width - 1, 40), Math.Min(bitmap.Height - 1, 120));
            if (probe.ToArgb() == Color.Black.ToArgb() && corner.ToArgb() == Color.Black.ToArgb())
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static void Write(string line)
    {
        Log.AppendLine(line);
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
            Console.WriteLine(line);
        }
        catch
        {
            // Console absente (application WinExe) : le fichier suffit.
        }
    }

    private static async Task Step(string name, Func<Task> action)
    {
        DateTime start = DateTime.Now;
        int before = AppDialogs.HeadlessMessages.Count;
        await action();
        List<string> shown = AppDialogs.HeadlessMessages.Skip(before).ToList();
        if (shown.Any(m => m.StartsWith("ERREUR", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Étape « {name} » : " + string.Join(" | ", shown));
        }

        Write($"  OK  {name} ({(DateTime.Now - start).TotalMilliseconds:0} ms)" +
              (shown.Count > 0 ? "  [" + string.Join(" | ", shown) + "]" : string.Empty));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Vérification échouée : " + message + " [" + string.Join(" | ", AppDialogs.HeadlessMessages.TakeLast(3)) + "]");
        }
    }

    private static async Task Scenario(string apiUrl, string? screensDir, Form hostForm)
    {
        Write($"SELFTEST Pressing Desktop — API {apiUrl} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        if (screensDir is not null)
        {
            Directory.CreateDirectory(screensDir);
        }

        using PressingApiClient api = new(apiUrl);
        int unauthorized = 0;
        api.Unauthorized += (_, _) => unauthorized++;
        AppSession session = null!;
        string password = Environment.GetEnvironmentVariable("PRESSING_ADMIN_PASSWORD") ?? "Admin@2026!";

        await Step("Santé de l'API", async () =>
        {
            HealthInfoDto? health = await api.GetHealthAsync();
            Check(health is { Status: "ok" or "healthy" or "Healthy" or "OK" } || health is not null, "health");
        });

        await Step("Connexion admin", async () =>
        {
            AuthResponse? auth = await api.LoginStaffAsync("admin", password);
            Check(auth?.Staff is not null, "AuthResponse.Staff absent");
            session = new AppSession(api, auth!.Staff!);
            Check(session.Role == UserRole.Admin && session.IsManagerOrAdmin, "rôle admin attendu");
        });

        await Step("Mauvais mot de passe → message lisible", async () =>
        {
            using PressingApiClient other = new(apiUrl);
            try
            {
                await other.LoginStaffAsync("admin", "mauvais-mot-de-passe");
                throw new InvalidOperationException("la connexion aurait dû échouer");
            }
            catch (ApiException ex)
            {
                Check(!string.IsNullOrWhiteSpace(ex.Message), "message vide");
            }
        });

        FakeHost fake = new(session, hostForm);

        // ---- Fenêtre de connexion (capture) ----
        await Step("FrmLogin (construction + capture)", async () =>
        {
            using PressingApiClient loginApi = new(apiUrl);
            using FrmLogin login = new(loginApi, apiUrl);
            await Capture(login, screensDir, "01-login");
        });

        // ---- Chaque vue enregistrée ----
        foreach (NavItem item in NavRegistry.Items)
        {
            await Step($"Vue « {item.Title} » : création + chargement + actualisation", async () =>
            {
                Check(item.IsAllowed(session.Role) || session.Role != UserRole.Admin, "admin doit tout voir");
                BaseView view = item.Create(fake);
                hostForm.Controls.Add(view);
                view.Dock = DockStyle.Fill;
                await view.LoadAsync();
                await view.RefreshAsync();
                await view.ReactivateAsync();
                await Capture(view, screensDir, "view-" + item.Key);
                hostForm.Controls.Remove(view);
                view.Dispose();
            });
        }

        // ---- Scénario métier complet ----
        ClientDto client = null!;
        CategoryDto category = null!;
        CategoryPriceDto price = null!;
        OrderDto order = null!;

        await Step("Création d'un client (même requête que le dialogue)", async () =>
        {
            string suffix = DateTime.Now.ToString("HHmmss");
            client = (await api.CreateClientAsync(new CreateClientRequest(
                "Auto", "Test" + suffix, "07" + suffix + "12", "07" + suffix + "12", $"auto{suffix}@example.com",
                "Cocody", "Créé par l'auto-test", "MotDePasse@2026", false)))!;
            Check(client.Id > 0, "client non créé");
            ClientDto? updated = await api.UpdateClientAsync(client.Id, new UpdateClientRequest(
                client.FirstName, client.LastName, client.Phone, client.WhatsApp, client.Email, client.Address, "Modifié", true));
            Check(updated?.Notes == "Modifié", "mise à jour du client");
            ClientDto? access = await api.SetClientPasswordAsync(client.Id, new SetClientPasswordRequest("NouveauMdp@2026", true, false));
            Check(access?.WebAccessEnabled == true, "accès web");
        });

        await Step("Recherche client (lookup + liste paginée)", async () =>
        {
            List<ClientDto>? found = await api.LookupClientsAsync(client.LastName, 8);
            Check(found?.Any(c => c.Id == client.Id) == true, "lookup");
            PagedResult<ClientDto>? page = await api.SearchClientsAsync(client.Phone, 1, 25);
            Check(page?.Items.Any(c => c.Id == client.Id) == true, "recherche");
        });

        await Step("Catalogue", async () =>
        {
            IReadOnlyList<CategoryDto> catalog = await session.GetCatalogAsync(true);
            Check(catalog.Count > 0, "catalogue vide");
            category = catalog.First(c => c.Prices.Any(p => p.IsActive));
            price = category.Prices.First(p => p.IsActive);
        });

        await Step("Écran Nouveau dépôt : dépôt complet piloté par la vue", async () =>
        {
            NewOrderView view = new(fake);
            hostForm.Controls.Add(view);
            view.Dock = DockStyle.Fill;
            await view.LoadAsync();
            view.ApplyParameter(client);
            Type t = typeof(NewOrderView);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            SelectBox cat = (SelectBox)t.GetField("_category", flags)!.GetValue(view)!;
            cat.SelectedIndex = 0;
            t.GetMethod("AddOrUpdateLine", flags)!.Invoke(view, null);
            ((StepperBox)t.GetField("_quantity", flags)!.GetValue(view)!).Number = 3;
            t.GetMethod("AddOrUpdateLine", flags)!.Invoke(view, null);
            ((ToggleBox)t.GetField("_globalExpress", flags)!.GetValue(view)!).Checked = false;
            ((InputBox)t.GetField("_deposit", flags)!.GetValue(view)!).Text = "500";
            await (Task)t.GetMethod("RequestQuoteAsync", flags)!.Invoke(view, null)!;
            OrderQuoteDto? quote = (OrderQuoteDto?)t.GetField("_quote", flags)!.GetValue(view);
            Check(quote is { Total: > 0 }, "devis non calculé");
            await Capture(view, screensDir, "view-new-order-filled");
            await (Task)t.GetMethod("SubmitAsync", flags)!.Invoke(view, null)!;
            order = (OrderDto)t.GetField("_createdOrder", flags)!.GetValue(view)!;
            Check(order is not null && order.TicketNumber.StartsWith("PRESS-", StringComparison.Ordinal), "dépôt non créé");
            Check(order!.PaidAmount == 500, "acompte attendu 500, obtenu " + order.PaidAmount);
            Check(order.Items.Count == 2, "2 lignes attendues");
            await Capture(view, screensDir, "view-new-order-confirmation");
            hostForm.Controls.Remove(view);
            view.Dispose();
        });

        await Step("Détail commande + fiche client (vues)", async () =>
        {
            foreach ((string name, BaseView v) in new (string, BaseView)[]
                     {
                         ("order-detail", new OrderDetailView(fake, order.Id)),
                         ("client-detail", new ClientDetailView(fake, client.Id))
                     })
            {
                hostForm.Controls.Add(v);
                v.Dock = DockStyle.Fill;
                await v.LoadAsync();
                if (Environment.GetEnvironmentVariable("PRESSING_DUMP") == name)
                {
                    Dump(v, 0, 7);
                }

                await Capture(v, screensDir, "view-" + name);
                hostForm.Controls.Remove(v);
                v.Dispose();
            }
        });

        await Step("Devis serveur et code promo inconnu", async () =>
        {
            OrderQuoteDto? q = await api.QuoteOrderAsync(new OrderQuoteRequest(
                new[] { new CreateOrderItemRequest(category.Id, price.ServiceType, 2, null, true, null, null, null) },
                false, DeliveryType.HomeDelivery, 1000, 0, "CODE-INEXISTANT", 0, client.Id));
            Check(q is { PromotionValid: false } && q.ExpressTotal >= 0, "devis promo");
        });

        await Step("Retrait refusé tant que rien n'est prêt", async () =>
        {
            try
            {
                await api.PickupAsync(order.Id, new PickupRequest(
                    new[] { new PickupItemRequest(order.Items[0].Id, 1) }, 0, PaymentMethod.Cash, null, null));
                Check(false, "le retrait d'un article non prêt aurait dû être refusé");
            }
            catch (ApiException ex)
            {
                Check(!string.IsNullOrWhiteSpace(ex.Message), "message de refus vide");
                Write("       refus lisible : " + ex.Message);
            }
        });

        await Step("Statut d'un article puis de toute la commande", async () =>
        {
            OrderDto? o = await api.UpdateItemStatusAsync(order.Id, order.Items[0].Id, ItemStatus.InProgress);
            Check(o?.Items[0].Status == ItemStatus.InProgress, "article en traitement");
            o = await api.UpdateOrderStatusAsync(order.Id, ItemStatus.Ready);
            Check(o is not null && o.Items.All(i => i.Status == ItemStatus.Ready), "tout prêt");
            order = o!;
        });

        await Step("Encaissement du solde", async () =>
        {
            PaymentDto? p = await api.AddPaymentAsync(order.Id, new AddPaymentRequest(
                order.Balance - 100, PaymentMethod.MobileMoney, "REF-AUTO", "auto-test", null));
            Check(p is { Amount: > 0 }, "paiement");
            order = (await api.GetOrderAsync(order.Id))!;
            Check(order.Balance == 100, "solde attendu 100, obtenu " + order.Balance);
        });

        await Step("Dialogues (construction + capture)", async () =>
        {
            OrderDto fresh = (await api.GetOrderAsync(order.Id))!;
            using PaymentDialog pay = new(api, fresh);
            await Capture(pay, screensDir, "dialog-payment");
            using PickupDialog pick = new(api, fresh);
            await Capture(pick, screensDir, "dialog-pickup");
            using RefundDialog refund = new(api, fresh);
            await Capture(refund, screensDir, "dialog-refund");
            using ClientEditDialog edit = new(api, client);
            await Capture(edit, screensDir, "dialog-client");
            using CategoryDialog cat = new(api, category);
            await Capture(cat, screensDir, "dialog-category");
            using NotificationsDialog notifs = new(api, await api.GetNotificationsAsync(20) ?? new List<NotificationDto>());
            await Capture(notifs, screensDir, "dialog-notifications");
            using MessageDialog message = new("Confirmer l'annulation", "Cette commande sera annulée définitivement. Voulez-vous continuer ?",
                MessageKind.Warning, "Annuler la commande", "Retour", true);
            await Capture(message, screensDir, "dialog-message");
        });

        await Step("Clôture de caisse : aperçu, écart, brouillon, validation figée, PDF, dialogue et écran", async () =>
        {
            DateTime today = DateTime.Today;
            DateTime yesterday = today.AddDays(-1);
            CashClosingPreviewDto preview = (await api.PreviewCashClosingAsync(today))!;
            Check(preview.TotalExpected == preview.ExpectedCash + preview.ExpectedMobileMoney + preview.ExpectedCard
                  + preview.ExpectedTransfer + preview.ExpectedOther, "total de l'aperçu");

            using (CashClosingDialog fresh = new(api, preview))
            {
                await Capture(fresh, screensDir, "dialog-cash-closing");
            }

            if (preview.ExistingStatus is null)
            {
                try
                {
                    await api.CreateCashClosingAsync(new CashClosingRequest(today, 0, preview.ExpectedCash - 500, null));
                    Check(false, "un écart sans commentaire aurait dû être refusé");
                }
                catch (ApiException ex)
                {
                    Check(ex.Message.Contains("commentaire", StringComparison.OrdinalIgnoreCase), "message d'écart : " + ex.Message);
                }

                CashClosingDto draft = (await api.CreateCashClosingAsync(
                    new CashClosingRequest(today, 0, preview.ExpectedCash - 500, "Auto-test : 500 FCFA manquants")))!;
                Check(draft.Status == CashClosingStatus.Draft && draft.Variance == -500, "brouillon avec écart de −500");

                using (CashClosingDialog editing = new(api, preview, draft))
                {
                    await Capture(editing, screensDir, "dialog-cash-closing-variance");
                }

                CashClosingDto validated = (await api.ValidateCashClosingAsync(draft.Id))!;
                Check(validated.Status == CashClosingStatus.Validated && validated.ValidatedAt is not null, "validation");
                try
                {
                    await api.UpdateCashClosingAsync(draft.Id, new CashClosingRequest(today, 0, preview.ExpectedCash, null));
                    Check(false, "une clôture validée ne doit plus être modifiable");
                }
                catch (ApiException ex)
                {
                    Check(ex.StatusCode == System.Net.HttpStatusCode.BadRequest, "refus de modification : " + ex.Message);
                }

                byte[] pdf = await api.GetCashClosingPdfAsync(draft.Id);
                Check(pdf.Length > 1000 && pdf[0] == (byte)'%' && pdf[1] == (byte)'P', "PDF de la clôture");
            }

            CashClosingPreviewDto? previewYesterday = await api.PreviewCashClosingAsync(yesterday);
            if (previewYesterday is { ExistingStatus: null })
            {
                await api.CreateCashClosingAsync(new CashClosingRequest(yesterday, 20000, 20000 + previewYesterday.ExpectedCash, null));
            }

            Check((await api.GetCashClosingsAsync(yesterday, today))!.Count >= 1, "historique des clôtures");
            CashView cashView = new(fake);
            hostForm.Controls.Add(cashView);
            cashView.Dock = DockStyle.Fill;
            await cashView.LoadAsync();
            await Capture(cashView, screensDir, "view-cash-closings");
            hostForm.Controls.Remove(cashView);
            cashView.Dispose();
        });

        await Step("Retrait partiel avec encaissement du reste", async () =>
        {
            OrderDto fresh = (await api.GetOrderAsync(order.Id))!;
            OrderItemDto first = fresh.Items[0];
            OrderDto? o = await api.PickupAsync(order.Id, new PickupRequest(
                new[] { new PickupItemRequest(first.Id, first.Quantity) }, 100, PaymentMethod.Cash, null, "retrait partiel"));
            Check(o is not null && o.Balance == 0, "solde nul après retrait");
            Check(o!.Pickups.Count == 1, "un retrait enregistré");
            OrderItemDto second = o.Items[1];
            o = await api.PickupAsync(order.Id, new PickupRequest(
                new[] { new PickupItemRequest(second.Id, second.Quantity) }, 0, PaymentMethod.Cash, null, null));
            Check(o?.Status == OrderStatus.Completed, "commande terminée, obtenu " + o?.Status);
            order = o!;
        });

        await Step("Reçu PDF (téléchargement)", async () =>
        {
            string path = await ReceiptService.DownloadAsync(api, order.Id, order.TicketNumber);
            Check(new FileInfo(path).Length > 500, "PDF trop petit");
            Write("       reçu : " + path + " (" + new FileInfo(path).Length + " octets)");
        });

        await Step("Reçu par e-mail", async () =>
        {
            MessageResponse? response = await api.EmailReceiptAsync(order.Id);
            Check(response is not null, "réponse vide");
        });

        await Step("Recherche par ticket (global)", async () =>
        {
            OrderDto? byTicket = await api.GetOrderByTicketAsync(order.TicketNumber);
            Check(byTicket?.Id == order.Id, "ticket");
            string? normalized = TicketParser.Normalize(order.Id.ToString());
            Check(normalized is not null && normalized.StartsWith("PRESS-", StringComparison.Ordinal), "normalisation");
            Check(await api.GetOrderByTicketAsync("PRESS-1999-99999") is null, "ticket inexistant → null");
        });

        await Step("Commandes filtrées, caisse, impayés, tableau de bord", async () =>
        {
            PagedResult<OrderSummaryDto>? list = await api.SearchOrdersAsync(OrderStatus.Completed, null, DateTime.Today, DateTime.Today, null, false, false, true, 1, 25);
            Check(list is { Total: > 0 }, "commandes terminées du jour");
            PagedResult<PaymentDto>? payments = await api.GetPaymentsAsync(DateTime.Today, DateTime.Today, null, null, 1, 1000);
            Check(payments is { Total: >= 2 }, "paiements du jour");
            await api.GetOutstandingOrdersAsync(200);
            DashboardDto? dash = await api.GetDashboardAsync();
            Check(dash is not null && dash.Last7Days.Count > 0, "dashboard");
            int unread = await api.GetUnreadNotificationCountAsync();
            Write("       notifications non lues : " + unread);
        });

        OrderDto cancelOrder = null!;
        await Step("Annulation et remboursement (Manager/Admin)", async () =>
        {
            OrderDto? created = await api.CreateOrderAsync(new CreateOrderRequest(
                client.Id, new[] { new CreateOrderItemRequest(category.Id, price.ServiceType, 1, null, false, null, "à annuler", null) },
                false, null, DeliveryType.StorePickup, null, 0, 0, null, 0, null, 200, PaymentMethod.Cash, null));
            Check(created is not null, "commande à annuler");
            PaymentDto? refund = await api.RefundAsync(created!.Id, new RefundRequest(100, PaymentMethod.Cash, "auto-test"));
            Check(refund is { Type: PaymentType.Refund }, "remboursement");
            cancelOrder = (await api.CancelOrderAsync(created.Id, "Annulation de l'auto-test"))!;
            Check(cancelOrder.Status == OrderStatus.Cancelled, "annulée");
        });

        await Step("Tarifs : création et modification d'une catégorie", async () =>
        {
            string name = "Catégorie test " + DateTime.Now.ToString("HHmmss");
            CategoryDto? created = await api.CreateCategoryAsync(new CreateCategoryRequest(name, string.Empty, 99,
                new[] { new UpdateCategoryPriceRequest(ServiceType.Washing, 1234, 200, 2, true) }));
            Check(created is not null && created.Prices.Any(p => p.Price == 1234), "création");
            CategoryDto? updated = await api.UpdateCategoryAsync(created!.Id, new UpdateCategoryRequest(name, created.Icon, 99, false,
                new[] { new UpdateCategoryPriceRequest(ServiceType.Washing, 1500, 250, 3, true) }));
            Check(updated is { IsActive: false }, "désactivation");
            session.InvalidateCatalog();
        });

        await Step("Administration : promotions, rapport, personnel, paramètres, sauvegarde", async () =>
        {
            string tag = DateTime.Now.ToString("HHmmss");
            PromotionDto? promo = await api.CreatePromotionAsync(new CreatePromotionRequest("AUTO" + tag, "Auto-test", PromotionType.Percentage, 10, 0,
                DateTime.Today, DateTime.Today.AddDays(30).AddDays(1).AddSeconds(-1), 0, true));
            Check(promo is { Code: not null } && promo.Code == "AUTO" + tag, "promotion créée");
            PromotionDto? promoUpdated = await api.UpdatePromotionAsync(promo!.Id, new UpdatePromotionRequest(promo.Code!, "Modifiée", PromotionType.FixedAmount, 500, 1000,
                promo.ValidFrom, promo.ValidTo, 5, false));
            Check(promoUpdated is { Description: "Modifiée", IsActive: false, MaxUses: 5 }, "promotion modifiée");

            ReportDto? report = await api.GetReportAsync(DateTime.Today.AddDays(-29), DateTime.Today);
            Check(report is { Daily.Count: > 0 }, "rapport");
            byte[] csv = await api.ExportReportCsvAsync(DateTime.Today.AddDays(-29), DateTime.Today);
            Check(csv.Length > 50, "CSV vide");

            StaffUserDto? member = await api.CreateStaffAsync(new CreateStaffRequest("Auto Test " + tag, "auto" + tag, null, UserRole.Cashier, "MotDePasse@2026"));
            Check(member is not null && member.Id > 0, "membre créé");
            StaffUserDto? memberUpdated = await api.UpdateStaffAsync(member!.Id, new UpdateStaffRequest(member.FullName, member.Username, "auto@example.com", UserRole.Manager, false));
            Check(memberUpdated is { Role: UserRole.Manager, IsActive: false }, "membre modifié");
            Check((await api.SetStaffPasswordAsync(member.Id, "AutreMdp@2026")) is not null, "mot de passe");

            SettingsView settings = new(fake);
            hostForm.Controls.Add(settings);
            settings.Dock = DockStyle.Fill;
            await settings.LoadAsync();
            foreach (string section in new[] { "emails", "receipts", "backups", "audit", "loyalty", "orders", "shop" })
            {
                await settings.ShowSectionAsync(section);
                await Capture(settings, screensDir, "settings-" + section);
            }

            string? original = settings.GetFieldValue(SettingsKeys.ShopSlogan);
            settings.SetFieldValue(SettingsKeys.ShopSlogan, "Slogan auto-test " + tag);
            Check(await settings.SaveChangesAsync() == 1, "un seul paramètre modifié");
            Check((await api.GetSettingsAsync())!.Values[SettingsKeys.ShopSlogan] == "Slogan auto-test " + tag, "paramètre enregistré");
            settings.SetFieldValue(SettingsKeys.ShopSlogan, original ?? string.Empty);
            Check(await settings.SaveChangesAsync() == 1, "paramètre remis");
            hostForm.Controls.Remove(settings);
            settings.Dispose();

            BackupResultDto? backup = await api.CreateBackupAsync();
            Check(backup is { IntegrityOk: true, Size: > 0 }, "sauvegarde");
            Check((await api.GetBackupsAsync())!.Any(b => b.FileName == backup!.FileName), "liste des sauvegardes");
            EmailMessageDto? test = await api.SendTestEmailAsync("auto@example.com");
            Check(test is not null, "e-mail de test");
            Check((await api.GetAuditAsync(1, 10, null)) is { Total: > 0 }, "audit");

            using PromotionDialog promoDialog = new(api, promoUpdated);
            await Capture(promoDialog, screensDir, "dialog-promotion");
            using StaffDialog staffDialog = new(api, memberUpdated, session.Staff.Id);
            await Capture(staffDialog, screensDir, "dialog-staff");
            using StaffPasswordDialog pwdDialog = new(api, memberUpdated!);
            await Capture(pwdDialog, screensDir, "dialog-staff-password");
        });

        await Step("Changement de mot de passe : refus d'un mauvais mot de passe actuel", async () =>
        {
            try
            {
                await api.ChangeStaffPasswordAsync("pas-le-bon", "UnNouveauMdp@2026");
                Check(false, "aurait dû être refusé");
            }
            catch (ApiException ex)
            {
                Check(!string.IsNullOrWhiteSpace(ex.Message), "message vide");
                Write("       refus lisible : " + ex.Message);
            }
        });

        // Coque complète à plusieurs tailles de fenêtre (px logiques, DESIGN.md §6) : PRESSING_TEST_SIZES="1280x760,1600x900".
        foreach (Size logical in ShellSizes())
        {
            string suffix = "-" + logical.Width;
            await Step($"Coque principale FrmMain {logical.Width}×{logical.Height} : navigation dans tous les écrans", async () =>
            {
                FrmMain.AllowOversize = true;
                using FrmMain main = new(session)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-5000, -5000),
                    ShowInTaskbar = false
                };
                main.WindowState = FormWindowState.Normal;
                main.Show();
                ResizeWindow(main, new Size(Theme.S(logical.Width), Theme.S(logical.Height)));
                await Task.Delay(1500);
                foreach (NavItem item in NavRegistry.Items.Where(i => i.IsAllowed(session.Role)))
                {
                    main.Navigate(item.Key);
                    await Task.Delay(1100);
                    await Capture(main, screensDir, "shell-" + item.Key + suffix);
                }

                main.OpenOrder(order.Id);
                await Task.Delay(1200);
                await Capture(main, screensDir, "shell-order-detail" + suffix);
                main.OpenClient(client.Id);
                await Task.Delay(1200);
                await Capture(main, screensDir, "shell-client-detail" + suffix);
                main.GoBack();
                await Task.Delay(800);
                main.Close();
            });
        }

        await Step("Listes déroulantes : fermeture répétée sans objet libéré", async () =>
        {
            using SelectBox box = new();
            hostForm.Controls.Add(box);
            box.SetItems(new object[] { "Chemise", "Pantalon" }, 0);

            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(SelectBox).GetMethod("OpenDropDown", flags)!.Invoke(box, null);
            FieldInfo dropField = typeof(SelectBox).GetField("_drop", flags)!;
            ToolStripDropDown drop = (ToolStripDropDown)dropField.GetValue(box)!;
            Check(drop.Visible, "liste non ouverte");

            drop.Close();
            Check(dropField.GetValue(box) is null, "référence de liste conservée après fermeture");
            Check(!drop.IsDisposed, "liste libérée pendant la fermeture");

            // Une sélection peut provoquer une seconde fermeture (appel explicite + fermeture automatique).
            drop.Close();
            Check(!drop.IsDisposed, "liste libérée avant la seconde fermeture");

            await Task.Delay(50);
            Check(drop.IsDisposed, "liste non libérée après traitement");
            hostForm.Controls.Remove(box);
        });

        await Step("Session expirée : événement Unauthorized", async () =>
        {
            using PressingApiClient expired = new(apiUrl) { Token = "jeton-invalide" };
            bool raised = false;
            expired.Unauthorized += (_, _) => raised = true;
            try
            {
                await expired.GetDashboardAsync();
                Check(false, "aurait dû échouer");
            }
            catch (ApiException ex)
            {
                Check(ex.IsUnauthorized && raised, "401 + événement");
            }
        });

        Check(AppDialogs.HeadlessMessages.All(m => !m.StartsWith("ERREUR", StringComparison.Ordinal)), "boîtes d'erreur affichées");
        Write($"  Total : {unauthorized} événement(s) Unauthorized sur la session principale.");
    }

    private static void Dump(Control c, int depth, int max)
    {
        Write(new string(' ', depth * 2) + c.GetType().Name + " " + c.Name + " " + c.Bounds + " vis=" + c.Visible + (c is Label l ? " '" + l.Text + "'" : string.Empty));
        if (depth < max)
        {
            foreach (Control child in c.Controls)
            {
                Dump(child, depth + 1, max);
            }
        }
    }

    private static async Task Capture(Control control, string? dir, string name)
    {
        if (dir is null)
        {
            return;
        }

        bool temporary = false;
        if (control is Form form && !form.Visible)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-5000, -5000);
            form.ShowInTaskbar = false;
            form.Show();
            temporary = true;
        }

        await Task.Delay(250);
        control.Update();
        Size size = control.Size;
        if (size.Width < 10 || size.Height < 10)
        {
            return;
        }

        using Bitmap bitmap = (control is Form shown ? PrintForm(shown) : null) ?? new Bitmap(size.Width, size.Height);
        if (control is not Form || bitmap.Width != size.Width || bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A == 0)
        {
            control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        }

        bitmap.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        if (temporary && control is Form f)
        {
            f.Hide();
        }
    }

    private sealed class FakeHost : IAppHost
    {
        private readonly Form _window;

        public FakeHost(AppSession session, Form window)
        {
            Session = session;
            _window = window;
        }

        public AppSession Session { get; }
        public PressingApiClient Api => Session.Api;
        public Form Window => _window;
        public void Navigate(string key, object? parameter = null) => Write("       [host] Navigate " + key);
        public void OpenOrder(int orderId) => Write("       [host] OpenOrder " + orderId);
        public void OpenClient(int clientId) => Write("       [host] OpenClient " + clientId);
        public void OpenView(string navKey, Func<BaseView> factory) => Write("       [host] OpenView " + navKey);
        public void GoBack() => Write("       [host] GoBack");
        public void StartNewOrder(ClientDto? client = null) => Write("       [host] StartNewOrder");
        public Task RefreshBadgesAsync() => Task.CompletedTask;
        public void Toast(string message, MessageKind kind = MessageKind.Success) => Write($"       [toast {kind}] {message}");
    }
}
