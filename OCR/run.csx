#r "Newtonsoft.Json"
#r "System.Drawing"
#r "System.IO"
#r "System.Web"

using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using System.Web;

using Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Vision.Contract;

using System.Drawing;

private static JsonSerializerSettings _jsonSettings;
private static SearchServiceClient _serviceClient;
private static ISearchIndexClient _indexClient;
private static string TextAnalyticsAPIKey = "XXXXXXXXX";
private static string SearchServiceName = "XXXXXXXXX";
private static string SearchServiceAPIKey = "XXXXXXXXX";
private static string IndexName = "XXXXXXXXX";
private static string KeyField = "metadata_storage_name";
private static string OcrField = "ocr";

private static string content = string.Empty;
private static int currentPage = 1;
private static int imageCount = 0;

public static string SubscriptionKey = "XXXXXXXXX";
public static VisionServiceClient VisionServiceClient;

public async static void Run(Stream myBlob, string name, TraceWriter log)
{
    log.Info($"C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

    // Extract all the images from the PDF
    var images = PdfImageExtractor.ExtractImages(myBlob, name, log);
    string mergedContent = string.Empty;

    // Init the Azure Search Client
    _serviceClient = new SearchServiceClient(SearchServiceName, new SearchCredentials(SearchServiceAPIKey));
    _indexClient = _serviceClient.Indexes.GetClient(IndexName);
    List<IndexAction> indexOperations = new List<IndexAction>();

    try
    {
        foreach (var filename in images.Keys)
        {
            // Each image will have a filename associated with it
            log.Info($"{filename}");

            using (var stream = new MemoryStream())
            {
                images[filename].Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Seek(0, SeekOrigin.Begin);
                string content = string.Empty;

                // Call Cognitive Services Vision API to extract text from image and append to the total content
                content = await ExtractTextAsync(stream, log);
                if (content != string.Empty)
                    mergedContent += content + "\r\n";
            }
        }

        // Upload results to Azure Search
        if (mergedContent != string.Empty)
        {
            var doc = new Document();
            string currentId = HttpServerUtility.UrlTokenEncode(Encoding.UTF8.GetBytes(name));

            doc.Add(KeyField, currentId);
            doc.Add(OcrField, mergedContent);
            indexOperations.Add(IndexAction.MergeOrUpload(doc));

            log.Info($"Uploading document to Azure Search using ID: {currentId}");
            UploadDocuments(indexOperations);
            indexOperations.Clear();
        }
    }
    catch (Exception ex)
    {
        log.Info($"Error: {ex.Message}");
    }
    log.Info($"Content: {mergedContent}");
}

public static async Task<string> ExtractTextAsync(Stream imageStream, TraceWriter log)
{
    // Extract the content from the image
    VisionServiceClient = new VisionServiceClient(SubscriptionKey);
    log.Info("VisionServiceClient is created...");
    string content = string.Empty;

    try
    {
        OcrResults ocrResult = await VisionServiceClient.RecognizeTextAsync(imageStream, "en");

        foreach (var region in ocrResult.Regions)
        {
            foreach (var line in region.Lines)
            {
                foreach (var word in line.Words)
                {
                    content += word.Text + " ";
                }
                content += "\r\n";
            }
        }
    }
    catch (Exception ex)
    {
        log.Info($"Error: {ex.Message}");
    }

    return content;
}


public static class PdfImageExtractor
{
    #region Methods

    #region Public Methods

    /// <summary>Extracts all images (of types that iTextSharp knows how to decode) from a PDF file.</summary>
    public static Dictionary<string, System.Drawing.Image> ExtractImages(Stream myBlob, string filename, TraceWriter log)
    {
        var images = new Dictionary<string, System.Drawing.Image>();

        try
        {
            using (var reader = new PdfReader(myBlob))
            {
                var parser = new PdfReaderContentParser(reader);
                ImageRenderListener listener = null;

                for (var i = 1; i <= reader.NumberOfPages; i++)
                {
                    parser.ProcessContent(i, (listener = new ImageRenderListener()));
                    var index = 1;

                    if (listener.Images.Count > 0)
                    {
                        log.Info($"Found {listener.Images.Count} images on page {i}.");

                        foreach (var pair in listener.Images)
                        {
                            images.Add(string.Format("{0}_Page_{1}_Image_{2}{3}",
                                System.IO.Path.GetFileNameWithoutExtension(filename), i.ToString("D4"), index.ToString("D4"), pair.Value), pair.Key);
                            index++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Info($"Error: {ex.Message}");
        }

        return images;
    }

    #endregion Public Methods

    #endregion Methods
}

internal class ImageRenderListener : IRenderListener
{
    #region Fields

    Dictionary<System.Drawing.Image, string> images = new Dictionary<System.Drawing.Image, string>();

    #endregion Fields

    #region Properties

    public Dictionary<System.Drawing.Image, string> Images
    {
        get { return images; }
    }

    #endregion Properties

    #region Methods

    #region Public Methods

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
                System.Drawing.Image drawingImage = image.GetDrawingImage();

                string extension = ".";

                if (filter == PdfName.DCTDECODE)
                {
                    extension += PdfImageObject.ImageBytesType.JPG.FileExtension;
                }
                else if (filter == PdfName.JPXDECODE)
                {
                    extension += PdfImageObject.ImageBytesType.JP2.FileExtension;
                }
                else if (filter == PdfName.FLATEDECODE)
                {
                    extension += PdfImageObject.ImageBytesType.PNG.FileExtension;
                }
                else if (filter == PdfName.LZWDECODE)
                {
                    extension += PdfImageObject.ImageBytesType.CCITT.FileExtension;
                }

                /* Rather than struggle with the image stream and try to figure out how to handle 
                    * BitMapData scan lines in various formats (like virtually every sample I've found 
                    * online), use the PdfImageObject.GetDrawingImage() method, which does the work for us. */
                this.Images.Add(drawingImage, extension);
            }
            catch (Exception)
            {
                //log.Info(ex.Message);
            }
        }
    }

    public void RenderText(TextRenderInfo renderInfo) { }

    #endregion Public Methods

    #endregion Methods
}

private static void UploadDocuments(List<IndexAction> indexOperations)
{
    try
    {
        _indexClient.Documents.Index(new IndexBatch(indexOperations));
    }
    catch (IndexBatchException e)
    {
        // Sometimes when your Search service is under load, indexing will fail for some of the documents in
        // the batch. Depending on your application, you can take compensating actions like delaying and
        // retrying. For this simple demo, we just log the failed document keys and continue.
        Console.WriteLine(
        "Failed to index some of the documents: {0}",
                String.Join(", ", e.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key)));
    }

}
