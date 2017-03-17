using Microsoft.WindowsAzure.Storage;
using System;
using System.Net;
using TikaOnDotNet.TextExtraction;

private static string accountName = "xxx";
private static string accountKey = "xxx==";
private static string container = "xxx";
private static string filename = "xxx.pdf";

public static void Run(HttpRequestMessage req, TraceWriter log)
{
    log.Info("C# HTTP trigger function processed a request.");

    string blobConectionString = "DefaultEndpointsProtocol=https;AccountName=" + accountName + ";AccountKey=" + accountKey + ";";

    var blobStorageAccount = CloudStorageAccount.Parse(blobConectionString);
    var blobBlobClient = blobStorageAccount.CreateCloudBlobClient();
    var blobContainer = blobBlobClient.GetContainerReference(container);
    var blockBlob = blobContainer.GetBlockBlobReference(filename);

    blockBlob.FetchAttributes();
    long fileByteLength = blockBlob.Properties.Length;
    byte[] fileContent = new byte[fileByteLength];
    for (int i = 0; i < fileByteLength; i++)
    {
        fileContent[i] = 0x20;
    }
    blockBlob.DownloadToByteArray(fileContent, 0);

    var textExtractor = new TextExtractor();
    var result = textExtractor.Extract(fileContent);

    log.Info("Content Type: " + result.ContentType);
    log.Info("\n\n" + result.Text.Trim());

}