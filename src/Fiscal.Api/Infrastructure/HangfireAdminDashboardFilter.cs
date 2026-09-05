using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Fiscal.Api.Infrastructure;

/// <summary>
/// Exige JWT de admin para o dashboard do Hangfire (via header Authorization ou
/// ?access_token= — o handler JwtBearer é instruído a ler da query em /hangfire).
/// Substitui o dashboard aberto que existia antes do painel admin.
/// </summary>
public class HangfireAdminDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();

        // Sessão já autenticada pelo middleware (header).
        if (http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("admin"))
            return true;

        // Acesso via browser com ?access_token= — o pipeline default autentica
        // só o scheme ApiKey, então validamos o Bearer explicitamente aqui.
        var result = http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)
            .GetAwaiter().GetResult();
        return result.Succeeded && result.Principal.IsInRole("admin");
    }
}
