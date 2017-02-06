#r "System.Drawing"
#r "System.IO"
#r "System.Web"

using System.Drawing;
using System.Text;
using System.Web;

using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Vision.Contract;

private const string SearchServiceName = "XXXXXXXXX";
private const string SearchServiceAPIKey = "XXXXXXXXX";
private const string IndexName = "XXXXXXXXX";
private const string KeyField = "metadata_storage_name";
private const string OcrField = "ocr";
private const string VisionServiceSubscriptionKey = "XXXXXXXXX";

public async static void Run(Stream blob, string blobName, TraceWriter log)
{
    log.Info($"C# Blob trigger function Processed blob\n Name:{blobName} \n Size: {blob.Length} Bytes");
    log.Info($"Extracting images from the PDF");
    List<Image> images = PdfImageExtractor.ExtractImages(blob, blobName, log);

    StringBuilder extractedText = new StringBuilder();
    try
    {
        log.Info($"Extracting text from images");
        VisionServiceClient visionServiceClient = new VisionServiceClient(VisionServiceSubscriptionKey);
        foreach (Image image in images)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Seek(0, SeekOrigin.Begin);

                await ExtractTextAsync(visionServiceClient, stream, extractedText, log);
            }
        }

        if (extractedText.Length != 0)
        {
            SearchServiceClient serviceClient = new SearchServiceClient(SearchServiceName, new SearchCredentials(SearchServiceAPIKey));
            ISearchIndexClient indexClient = serviceClient.Indexes.GetClient(IndexName);

            string documentId = HttpServerUtility.UrlTokenEncode(Encoding.UTF8.GetBytes(blobName));
            log.Info($"Uploading document to Azure Search using ID: {documentId}");
            UploadToAzureSeearch(indexClient, documentId, log, extractedText.ToString());
        }
    }
    catch (Exception ex)
    {
        log.Info($"Error: {ex.Message}");
    }
}

private static async void UploadToAzureSeearch(ISearchIndexClient indexClient, string documentId, TraceWriter log, string extractedText)
{
    var document = new Document();
    document.Add(KeyField, documentId);
    document.Add(OcrField, extractedText);

    var indexOperations = new List<IndexAction>()
    {
        IndexAction.MergeOrUpload(document)
    };

    try
    {
        await indexClient.Documents.IndexAsync(new IndexBatch(indexOperations));
    }
    catch (IndexBatchException e)
    {
        // Sometimes when your Search service is under load, indexing will fail for some of the documents in
        // the batch. Depending on your application, you can take compensating actions like delaying and
        // retrying. For this simple demo, we just log the failed document keys and continue.
        log.Info("Failed to index some of the documents: " + string.Join(", ", e.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key)));
    }
}

private static async Task ExtractTextAsync(VisionServiceClient visionServiceClient, Stream imageStream, StringBuilder text, TraceWriter log)
{
    try
    {
        OcrResults ocrResult = await visionServiceClient.RecognizeTextAsync(imageStream, "en");
        foreach (var region in ocrResult.Regions)
        {
            foreach (var line in region.Lines)
            {
                text.Append(string.Join(" ", line.Words.Select(w => w.Text)));
                text.AppendLine();
            }
        }
    }
    catch (Exception ex)
    {
        log.Error($"Error: {ex.Message}");
    }
}

private static class PdfImageExtractor
{
    // Extracts all images (of types that iTextSharp knows how to decode) from a PDF file
    public static List<Image> ExtractImages(Stream myBlob, string filename, TraceWriter log)
    {
        var images = new List<Image>();
        try
        {
            using (var reader = new PdfReader(myBlob))
            {
                var parser = new PdfReaderContentParser(reader);
                var listener = new ImageRenderListener(log);

                for (var i = 1; i <= reader.NumberOfPages; i++)
                {
                    parser.ProcessContent(i, listener);
                    if (listener.Images.Count > 0)
                    {
                        log.Verbose($"Found {listener.Images.Count} images on page {i}.");
                        images.AddRange(listener.Images);
                        listener.Images.Clear();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"Error: {ex.Message}");
        }
        return images;
    }
}

class ImageRenderListener : IRenderListener
{
    private List<Image> _images = new List<Image>();
    private TraceWriter _log;

    public ImageRenderListener(TraceWriter log)
    {
        _log = log;
    }

    public List<Image> Images
    {
        get { return _images; }
    }

    public void BeginTextBlock() { }

    public void EndTextBlock() { }

    public void RenderImage(ImageRenderInfo renderInfo)
    {
        PdfImageObject image = renderInfo.GetImage();
        PdfName filter = (PdfName)image.Get(PdfName.FILTER);
        if (filter != null)
        {
            try
            {
                Image drawingImage = image.GetDrawingImage();
                _images.Add(drawingImage);
            }
            catch (Exception)
            {
                // _log.Error(e.Message);
            }
        }
    }

    public void RenderText(TextRenderInfo renderInfo) { }
}

