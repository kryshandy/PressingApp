using Pressing.Core.Messaging;
using Pressing.Core.Services;

namespace Pressing.Api.Infrastructure;

public static class ReminderServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre la messagerie et les rappels : options WhatsApp (section « WhatsApp » puis variables du .env),
    /// transport (Meta ou simulé), <see cref="ReminderService"/> et, sauf REMINDERS_ENABLED=false, la tâche de fond.
    /// </summary>
    public static IServiceCollection AddReminders(this IServiceCollection services, IConfiguration configuration)
    {
        WhatsAppOptions whatsAppOptions =
            configuration.GetSection("WhatsApp").Get<WhatsAppOptions>()
            ?? new WhatsAppOptions();
        // Variables du .env (même convention que EMAIL_* / SMTP_*), prioritaires sur la section « WhatsApp ».
        whatsAppOptions.Provider = EnvOr("WHATSAPP_PROVIDER", whatsAppOptions.Provider);
        whatsAppOptions.AccessToken = EnvOr("WHATSAPP_ACCESS_TOKEN", whatsAppOptions.AccessToken);
        whatsAppOptions.PhoneNumberId = EnvOr("WHATSAPP_PHONE_NUMBER_ID", whatsAppOptions.PhoneNumberId);
        whatsAppOptions.ApiVersion = EnvOr("WHATSAPP_API_VERSION", whatsAppOptions.ApiVersion);
        whatsAppOptions.WebhookVerifyToken = EnvOr("WHATSAPP_WEBHOOK_VERIFY_TOKEN", whatsAppOptions.WebhookVerifyToken);
        whatsAppOptions.AppSecret = EnvOr("WHATSAPP_APP_SECRET", whatsAppOptions.AppSecret);
        whatsAppOptions.TemplateReady = EnvOr("WHATSAPP_TEMPLATE_READY", whatsAppOptions.TemplateReady);
        whatsAppOptions.TemplateUncollected = EnvOr("WHATSAPP_TEMPLATE_UNCOLLECTED", whatsAppOptions.TemplateUncollected);
        whatsAppOptions.TemplateBalance = EnvOr("WHATSAPP_TEMPLATE_BALANCE", whatsAppOptions.TemplateBalance);
        whatsAppOptions.TemplateDue = EnvOr("WHATSAPP_TEMPLATE_DUE", whatsAppOptions.TemplateDue);
        whatsAppOptions.TemplateLanguage = EnvOr("WHATSAPP_TEMPLATE_LANGUAGE", whatsAppOptions.TemplateLanguage);

        services.AddSingleton(whatsAppOptions);

        if (whatsAppOptions.Provider.Equals("meta", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<IWhatsAppTransport, MetaWhatsAppTransport>();
        }
        else
        {
            services.AddSingleton<IWhatsAppTransport, SimulatedWhatsAppTransport>();
        }

        services.AddScoped<ReminderService>();

        // Rappels automatiques (WhatsApp / e-mail, clients ayant donné leur consentement). REMINDERS_ENABLED=false les coupe.
        if (!string.Equals(Environment.GetEnvironmentVariable("REMINDERS_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<ReminderBackgroundService>();
        }

        return services;
    }

    private static string EnvOr(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
