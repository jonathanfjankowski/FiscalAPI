using System.Diagnostics;
using System.Diagnostics.Metrics;
using Fiscal.Core.Enums;

namespace Fiscal.Core.Services;

/// <summary>
/// Métricas de negócio do FiscalAPI (OTel/Prometheus — roadmap item 4).
/// Instrumentos expostos via meter "FiscalAPI"; a API publica em /metrics
/// (Prometheus) e o Worker empurra por OTLP quando configurado.
/// </summary>
public class MetricasFiscais
{
    public const string NomeMedidor = "FiscalAPI";

    // Versão do assembly (VersionPrefix no Directory.Build.props) — nunca
    // hardcoded, senão o service_version do OTel/Prometheus fica defasado.
    public static readonly string VersaoServico =
        typeof(MetricasFiscais).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly Counter<long> _documentos;
    private readonly Histogram<double> _latenciaAutorizacao;
    private readonly Counter<long> _webhooks;
    private readonly Counter<long> _contingencia;
    private readonly Counter<long> _eventos;

    public MetricasFiscais(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(NomeMedidor);

        _documentos = meter.CreateCounter<long>(
            "fiscal_documentos", unit: "{documento}",
            description: "Documentos fiscais processados por tipo, status, UF e ambiente.");

        _latenciaAutorizacao = meter.CreateHistogram<double>(
            "fiscal_latencia_autorizacao", unit: "s",
            description: "Latência PENDENTE → AUTORIZADA por tipo e UF.");

        _webhooks = meter.CreateCounter<long>(
            "fiscal_webhooks", unit: "{entrega}",
            description: "Entregas de webhook por evento e resultado.");

        _contingencia = meter.CreateCounter<long>(
            "fiscal_contingencia", unit: "{documento}",
            description: "Documentos enviados para contingência por modo.");

        _eventos = meter.CreateCounter<long>(
            "fiscal_eventos", unit: "{evento}",
            description: "Eventos fiscais processados por tipo e status.");
    }

    public void DocumentoProcessado(TipoDocumento tipo, StatusDocumento status, string? uf, bool producao) =>
        _documentos.Add(1,
            new KeyValuePair<string, object?>("tipo", tipo.ToString()),
            new KeyValuePair<string, object?>("status", status.ToString()),
            new KeyValuePair<string, object?>("uf", uf ?? "n/d"),
            new KeyValuePair<string, object?>("ambiente", producao ? "producao" : "homologacao"));

    public void LatenciaAutorizacao(double segundos, TipoDocumento tipo, string? uf) =>
        _latenciaAutorizacao.Record(segundos,
            new KeyValuePair<string, object?>("tipo", tipo.ToString()),
            new KeyValuePair<string, object?>("uf", uf ?? "n/d"));

    public void WebhookEntregue(string evento, bool sucesso) =>
        _webhooks.Add(1,
            new KeyValuePair<string, object?>("evento", evento),
            new KeyValuePair<string, object?>("resultado", sucesso ? "sucesso" : "falha"));

    public void ContingenciaAcionada(string modo) =>
        _contingencia.Add(1, new KeyValuePair<string, object?>("modo", modo));

    public void EventoProcessado(string tipoEvento, string status) =>
        _eventos.Add(1,
            new KeyValuePair<string, object?>("tipo", tipoEvento),
            new KeyValuePair<string, object?>("status", status));
}
