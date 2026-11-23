using SkiaSharp;

namespace Fiscal.Pdf;

/// <summary>Geração de código de barras CODE-128 (DANFE) e QR Code (DANFCe).</summary>
internal static class PdfBarcodes
{
    /// <summary>Código de barras CODE-128 da chave de acesso (44 dígitos) em PNG.</summary>
    public static byte[]? GerarCodigoBarrasChave(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44 || chave.Any(c => !char.IsDigit(c)))
            return null;

        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = ZXing.BarcodeFormat.CODE_128,
            Options = new ZXing.Common.EncodingOptions
            {
                Width = 420,
                Height = 90,
                Margin = 0,
                PureBarcode = true,
            },
        };
        using var bitmap = writer.Write(chave);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Extrai a URL do QR Code do DANFCe do XML autorizado (infNFeSupl/qrCode).</summary>
    public static string? ExtrairQrCode(string? xmlAssinado)
    {
        if (string.IsNullOrWhiteSpace(xmlAssinado))
            return null;

        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xmlAssinado);
            var qr = doc.SelectSingleNode("//*[local-name()='qrCode']")?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(qr))
                return null;
            return qr.Replace("<![CDATA[", "").Replace("]]>", "").Trim();
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>QR Code em PNG a partir da URL de consulta do DANFCe.</summary>
    public static byte[] GerarQrCode(string conteudo)
    {
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(conteudo, QRCoder.QRCodeGenerator.ECCLevel.M);
        return new QRCoder.PngByteQRCode(data).GetGraphic(pixelsPerModule: 4);
    }
}
