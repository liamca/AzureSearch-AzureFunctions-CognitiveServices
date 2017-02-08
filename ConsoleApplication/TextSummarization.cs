using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json;

public class Function2
{
    private static string SearchServiceName = ConfigurationManager.AppSettings["SearchServiceName"];
    private static string SearchServiceAPIKey = ConfigurationManager.AppSettings["SearchServiceAPIKey"];

    private const string IndexName = "oregonbriefs";
    private const string KeyField = "metadata_storage_name";
    private const string SummaryField = "summary";
    private const string KeyPhrasesField = "keyPhrases";

    private static string TextAnalyticsAPIKey = ConfigurationManager.AppSettings["TextAnalyticsAPIKey"];
    private static Uri CognitiveServicesUri = new Uri(@"https://westus.api.cognitive.microsoft.com/text/analytics/v2.0/keyPhrases");

    private const int MaxSentencesInASummary = 10;
    private static Regex FindSentencesRegex = new Regex(@"(?<=[\.!\?])\s+", RegexOptions.Compiled | RegexOptions.Multiline);

    public static async Task Run(Stream myBlob, string name, TraceWriter log)
    {
        log.Info($"Text Processing beginning for {name} ({myBlob.Length} Bytes)");

        var reader = new PdfReader(myBlob);
        var extractionStrategy = new SimpleTextExtractionStrategy();

        log.Info($"Extracting text from the PDF");
        List<dynamic> pages = new List<dynamic>();
        StringBuilder content = new StringBuilder();
        for (int i = 1; i <= Math.Min(1000, reader.NumberOfPages); i++)
        {
            string page = PdfTextExtractor.GetTextFromPage(reader, i, extractionStrategy);
            content.AppendLine(page);
            pages.Add(new { id = i.ToString(), text = page.Substring(0, Math.Min(4096, page.Length))});            
        }

        log.Info($"Finding key phrases");
        Dictionary<string, int> keyPhrases = await GetKeyPhrases(pages, log);
        var top10Phrases = keyPhrases.OrderByDescending(pair => pair.Value).Take(10).Select(kp => kp.Key);

        log.Info($"Building summary");
        string summary = BuildSummary(content.ToString(), top10Phrases);

        SearchServiceClient serviceClient = new SearchServiceClient(SearchServiceName, new SearchCredentials(SearchServiceAPIKey));
        ISearchIndexClient indexClient = serviceClient.Indexes.GetClient(IndexName);        
        string documentId = HttpServerUtility.UrlTokenEncode(Encoding.UTF8.GetBytes(name));
        
        log.Info($"Uploading document to Azure Search using ID: {documentId}");
        await UploadToAzureSeearch(indexClient, documentId, keyPhrases.Keys.ToList(), summary, log);
    }

    private static async Task<Dictionary<string, int>> GetKeyPhrases(List<dynamic> documents, TraceWriter log)
    {
        Dictionary<string, int> keyPhrases = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);

        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", TextAnalyticsAPIKey);

        string requestPayload = JsonConvert.SerializeObject(new { documents = documents });
        var request = new HttpRequestMessage(HttpMethod.Post, CognitiveServicesUri)
        {
            Content = new StringContent(requestPayload, Encoding.UTF8, "application/json")
        };
        HttpResponseMessage response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            string error = response.Content?.ReadAsStringAsync().Result;
            log.Error("Request failed: " + error);
            return keyPhrases;
        }

        string content = await response.Content.ReadAsStringAsync();
        dynamic responsePayload = JsonConvert.DeserializeObject<dynamic>(content);
        foreach(dynamic document in responsePayload.documents)
        {
            foreach(string keyPhrase in document.keyPhrases)
            {
                int count;
                if(keyPhrases.TryGetValue(keyPhrase, out count))
                {
                    keyPhrases[keyPhrase] = count++;
                }
                else
                {
                    keyPhrases[keyPhrase] = 1;
                }
            }
        }
        return keyPhrases;
    }

    private static string BuildSummary(string content, IEnumerable<string> phrases)
    {
        string[] sentences = FindSentencesRegex.Split(content);

        StringBuilder summary = new StringBuilder();

        int count = 0;
        foreach(var sentence in sentences)
        {
            if(phrases.Any(p => sentence.Contains(p)) & count < MaxSentencesInASummary)
            {
                summary.AppendLine(sentence);
                count++;
            }
        }
        return summary.ToString();
    }

    private static async Task UploadToAzureSeearch(ISearchIndexClient indexClient, string documentId, List<string> keyPhrases, string summary, TraceWriter log)
    {
        var document = new Document();
        document.Add(KeyField, documentId);
        document.Add(SummaryField, summary);
        document.Add(KeyPhrasesField, string.Join(", ", keyPhrases));

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
}
