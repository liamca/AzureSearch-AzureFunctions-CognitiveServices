#r "Newtonsoft.Json"
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
using System.Web;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using iTextSharp.text.pdf;
using iTextSharp.text.pdf.parser;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

private static JsonSerializerSettings _jsonSettings;
private static SearchServiceClient _serviceClient;
private static ISearchIndexClient _indexClient;
private static string TextAnalyticsAPIKey = "XXXXXXXXXX";
private static string SearchServiceName = "XXXXXXXXXX";
private static string SearchServiceAPIKey = "XXXXXXXXXX";
private static string IndexName = "XXXXXXXXXX";
private static string KeyField = "metadata_storage_name";
private static string SummaryField = "summary";

public static void Run(Stream myBlob, string name, TraceWriter log)
{
    log.Info($"Text Processing beginning for {name} ({myBlob.Length} Bytes)");

    // Init the Azure Search Client
    _serviceClient = new SearchServiceClient(SearchServiceName, new SearchCredentials(SearchServiceAPIKey));
    _indexClient = _serviceClient.Indexes.GetClient(IndexName);
    List<IndexAction> indexOperations = new List<IndexAction>();
    string text = string.Empty;

    // Init the HTTPClient for Text Analytics and Azure Search client
    Uri _serviceUri = new Uri("https://westus.api.cognitive.microsoft.com");
    HttpClient _httpClient = new HttpClient();

    // Extract the text from the blob 
    PdfReader reader = new PdfReader(myBlob);
    for (int i = 1; i <= reader.NumberOfPages; i++)
        text += PdfTextExtractor.GetTextFromPage(reader, i, new SimpleTextExtractionStrategy());

    // Remove all non alphanumeric or end of sentence chars
    string pattern = @"[^a-zA-Z0-9?!. ]+";
    Regex r = new Regex(pattern, RegexOptions.Compiled);
    string parsedText = text.Replace("'", " ");
    parsedText = r.Replace(parsedText, " ");
    // At this point there will be a lot of double spaces so clean this up
    RegexOptions options = RegexOptions.None;
    Regex regex = new Regex("[ ]{2,}", options);
    parsedText = regex.Replace(parsedText, " ");
    parsedText = parsedText.Substring(0, Math.Min(4096, parsedText.Length));

    // Submit first 4KB of doc for processing to Text Analytics 
    _jsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented, // for readability, change to None for compactness
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        DateTimeZoneHandling = DateTimeZoneHandling.Utc
    };
    _jsonSettings.Converters.Add(new StringEnumConverter());
    _httpClient.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", TextAnalyticsAPIKey);
    string json = "{\"documents\":[{ \"id\":\"1\",\"text\":\"" + parsedText + "\"}]}";
    Uri uri = new Uri(_serviceUri, "/text/analytics/v2.0/keyPhrases");
    HttpResponseMessage response = SendRequest(_httpClient, HttpMethod.Post, uri, json);
    EnsureSuccessfulResponse(response);
    // Convert returned content to JSON
    var content = DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result).documents;

    // Find the sentences that match the top X phrases and put into a SummaryText string
    log.Info("Top 10 Key Phrases:");
    List<string> SummarizedContentList = new List<string>(); 
    for (int i=0; i<Math.Min(10,content[0].keyPhrases.Count);i++) {
        string KeyPhrase = ((string)content[0].keyPhrases[i]).ToString();
        SummarizedContentList.Add(FindSentence(KeyPhrase, parsedText));
        log.Info(KeyPhrase);
    }
    string SummaryText = SummaryText = string.Join("", SummarizedContentList.Distinct().ToArray()).Trim();

    log.Info("Summarized Text:");
    log.Info(SummaryText);

    // Upload results to Azure Search
    var doc = new Document();
    string currentId = HttpServerUtility.UrlTokenEncode(Encoding.UTF8.GetBytes(name));

    doc.Add(KeyField, currentId);
    doc.Add(SummaryField, SummaryText);
    indexOperations.Add(IndexAction.MergeOrUpload(doc));

    log.Info($"Uploading document to Azure Search using ID: {currentId}");
    UploadDocuments(indexOperations);
    indexOperations.Clear();


}

public static string SerializeJson(object value)
{
    return JsonConvert.SerializeObject(value, _jsonSettings);
}

public static T DeserializeJson<T>(string json)
{
    return JsonConvert.DeserializeObject<T>(json, _jsonSettings);
}

public static HttpResponseMessage SendRequest(HttpClient client, HttpMethod method, Uri uri, string json = null)
{
    UriBuilder builder = new UriBuilder(uri);
    string separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : "&";
    builder.Query = builder.Query.Trim();

    var request = new HttpRequestMessage(method, builder.Uri);

    if (json != null)
    {
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
    }

    return client.SendAsync(request).Result;
}

public static void EnsureSuccessfulResponse(HttpResponseMessage response)
{
    if (!response.IsSuccessStatusCode)
    {
        string error = response.Content == null ? null : response.Content.ReadAsStringAsync().Result;
        throw new Exception("Search request failed: " + error);
    }
}

private static string FindSentence(string phrase, string content)
{
    string sentence = string.Empty;
    content += ".";

    var regex = new Regex(string.Format("[^.!?;]*({0})[^.?!;]*[.?!;]", phrase));
    var results = regex.Matches(content);

    if (results.Count > 0)
        return results[0].ToString() + " ";

    return sentence;
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
